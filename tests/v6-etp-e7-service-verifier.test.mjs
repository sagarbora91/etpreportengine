import test from 'node:test';
import assert from 'node:assert/strict';
import verifier from '../www/etp-e7-service-verifier.js';
import {scopeKey,approvedPackage,api as authority} from './lib/v6-etp-e7-fixtures.mjs';

const profile=()=>authority.activateApprovedPackage(approvedPackage()).profile;
const binding={source:'ETP_VERIFIED',boundary:'SERVICE_ETP_V1',scopeKey,generationId:'etp_'+'7'.repeat(32),receiptId:'service-receipt-001'};
const hashes=Object.fromEntries(profile().reportIdentities.map(x=>[x.reportType,x.headerSignatureSha256]));
const report=(reportType,rows)=>({reportType,scopeKey,generationId:binding.generationId,receiptId:binding.receiptId,headerSignatureSha256:hashes[reportType],rows});
const reports=()=>({
  S003_REVENUE:report('S003_REVENUE',[
    {jobKey:'JOB-001',transactionValue:'Service Revenue',amountPaise:10000,evidenceId:'s003-row-001'},
    {jobKey:'JOB-002',transactionValue:'Part Revenue',amountPaise:25000,evidenceId:'s003-row-002'}
  ]),
  S004_TENDER_DETAILED:report('S004_TENDER_DETAILED',[
    {jobKey:'JOB-001',paymentValue:'Cash',amountPaise:10000,evidenceId:'s004-row-001'},
    {jobKey:'JOB-002',paymentValue:'UPI',amountPaise:25000,evidenceId:'s004-row-002'}
  ])
});
const request=overrides=>({scopeKey,binding,profile:profile(),reports:reports(),...overrides});

test('exact PII-free S003/S004 projections reconcile by case-sensitive job key',()=>{
  const out=verifier.verify(request());
  assert.equal(out.status,'VERIFIED');
  assert.deepEqual(out.summary,{jobCount:2,matchedCount:2,discrepancyCount:0});
  assert.equal(out.boundary,'SERVICE_ETP_V1');
  assert.equal(out.generationId,binding.generationId);
  assert.equal(Object.isFrozen(out.matches),true);
});

test('missing authority and source binding drift produce honest BLOCKED states',()=>{
  assert.equal(verifier.verify(request({profile:null})).code,'E7_AUTHORITY_DEFERRED');
  assert.equal(verifier.verify(request({binding:{...binding,generationId:'etp_'+'8'.repeat(32)}})).code,'E7_MANDATORY_REPORT_IDENTITY_MISMATCH');
  assert.equal(verifier.readiness({scopeKey,profile:profile(),binding:{...binding,receiptId:'different-receipt'}}).status,'READY');
  assert.equal(verifier.readiness({scopeKey,profile:profile(),binding:{...binding,boundary:'RETAIL_ETP_V1'}}).code,'E7_VERIFIED_BINDING_INVALID');
});

test('Retail scope/facts, header drift and missing mandatory reports are never borrowed or guessed',()=>{
  assert.equal(verifier.verify(request({scopeKey:'WLMHW|2026-27|2026-08-01..2026-08-31'})).code,'E7_SCOPE_INVALID');
  const signatureDrift=reports();signatureDrift.S003_REVENUE.headerSignatureSha256='f'.repeat(64);assert.equal(verifier.verify(request({reports:signatureDrift})).code,'E7_MANDATORY_REPORT_IDENTITY_MISMATCH');
  const missing=reports();delete missing.S004_TENDER_DETAILED;assert.equal(verifier.verify(request({reports:missing})).code,'E7_MANDATORY_REPORT_IDENTITY_MISMATCH');
});

test('unknown dictionary values and any extra PII field reject the entire projection',()=>{
  const unknown=reports();unknown.S003_REVENUE.rows[0].transactionValue='Probably Revenue';assert.equal(verifier.verify(request({reports:unknown})).code,'E7_PROJECTION_REJECTED');
  const pii=reports();pii.S004_TENDER_DETAILED.rows[0].customerMobile='9999999999';assert.equal(verifier.verify(request({reports:pii})).code,'E7_PROJECTION_REJECTED');
  const fuzzy=reports();fuzzy.S004_TENDER_DETAILED.rows[0].jobKey='job-001';const out=verifier.verify(request({reports:fuzzy}));assert.equal(out.status,'VARIANCE');assert.deepEqual(out.discrepancies.map(x=>x.discrepancyType),['MISSING_TENDER','MISSING_REVENUE']);
});

test('prototype-like exact keys remain data and cannot alter reconciliation structures',()=>{
  const edge=reports();edge.S003_REVENUE.rows[0].jobKey='constructor';edge.S004_TENDER_DETAILED.rows[0].jobKey='constructor';
  const out=verifier.verify(request({reports:edge}));assert.equal(out.status,'VERIFIED');assert.equal(out.summary.jobCount,2);
});

test('variance evidence and discrepancy records are immutable exact-source projections',()=>{
  const changed=reports();changed.S004_TENDER_DETAILED.rows[1].amountPaise=24000;
  const out=verifier.verify(request({reports:changed})),item=out.discrepancies[0];
  assert.equal(out.status,'VARIANCE');
  assert.equal(item.discrepancyType,'AMOUNT_VARIANCE');
  assert.equal(item.variancePaise,1000);
  assert.deepEqual(item.evidence.map(x=>x.reference),['s003-row-002','s004-row-002']);
  assert.equal(Object.isFrozen(item),true);
  assert.equal(Object.isFrozen(item.evidence),true);
  assert.throws(()=>{item.status='CLOSED';},TypeError);
});

test('approved optional snapshot identity is accepted only with exact status/SKU dictionaries',()=>{
  const withSnapshot=reports();withSnapshot.PENDING_SNAPSHOT=report('PENDING_SNAPSHOT',[{jobKey:'JOB-003',statusValue:'Pending',skuValue:'LABOUR',snapshotDate:'2026-08-25',evidenceId:'pending-row-003'}]);
  const out=verifier.verify(request({reports:withSnapshot}));assert.equal(out.status,'VERIFIED');assert.equal(out.snapshots.PENDING_SNAPSHOT[0].statusCode,'PENDING');assert.equal(out.snapshots.PENDING_SNAPSHOT[0].skuClass,'NON_STOCK_SERVICE_TOKEN');
  withSnapshot.PENDING_SNAPSHOT.rows[0].statusValue='Maybe delivered';assert.equal(verifier.verify(request({reports:withSnapshot})).code,'E7_PROJECTION_REJECTED');
});
