import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {createRequire} from 'node:module';
import intake from '../www/etp-e7-authority-intake.js';
import verifier from '../www/etp-e7-service-verifier.js';
import {
  scopeKey,
  api as authority,
  candidate,
  approval,
  approvedPackage
} from './lib/v6-etp-e7-fixtures.mjs';

const require=createRequire(import.meta.url);
const operationalApi=require('../www/etp-e7-service-operational.js');
const presentation=require('../www/etp-e7-presentation.js');
const now='2026-08-25T10:00:00.000Z';
const activeProfile=()=>authority.activateApprovedPackage(approvedPackage()).profile;
const binding=(generation='7',receiptId='service-receipt-001',key=scopeKey)=>({
  source:'ETP_VERIFIED',boundary:'SERVICE_ETP_V1',scopeKey:key,
  generationId:'etp_'+generation.repeat(32),receiptId
});
const hashes=()=>Object.fromEntries(activeProfile().reportIdentities.map(x=>[x.reportType,x.headerSignatureSha256]));
const report=(reportType,rows,b=binding())=>({
  reportType,scopeKey:b.scopeKey,generationId:b.generationId,receiptId:b.receiptId,
  headerSignatureSha256:hashes()[reportType],rows
});
const reports=(b=binding())=>({
  S003_REVENUE:report('S003_REVENUE',[
    {jobKey:'JOB-001',transactionValue:'Service Revenue',amountPaise:10000,evidenceId:'s003-row-001'},
    {jobKey:'JOB-002',transactionValue:'Part Revenue',amountPaise:25000,evidenceId:'s003-row-002'}
  ],b),
  S004_TENDER_DETAILED:report('S004_TENDER_DETAILED',[
    {jobKey:'JOB-001',paymentValue:'Cash',amountPaise:10000,evidenceId:'s004-row-001'},
    {jobKey:'JOB-002',paymentValue:'UPI',amountPaise:24000,evidenceId:'s004-row-002'}
  ],b)
});
const memoryStorage=(seed=null)=>{let value=seed,writes=0;return{
  getItem:()=>value,
  setItem:(_key,next)=>{value=next;writes++;},
  value:()=>value,writes:()=>writes
};};
const make=(storage=memoryStorage())=>{let liveBinding=binding(),profile=activeProfile();const made=operationalApi.create({
  storage,storageKey:'saagar.e7.privacy.acceptance.v1',verifier,
  authorityProvider:()=>profile,bindingProvider:()=>liveBinding,now:()=>now,reauth:()=>true,authorize:()=>true
});return{...made,storage,setBinding:value=>{liveBinding=value;},setProfile:value=>{profile=value;}};};
const verifyRequest=(overrides={})=>({
  scopeKey,actorId:'owner-1',actorRole:'Owner',operationId:'verify-001',
  reports:reports(),reauthenticatedAt:now,...overrides
});

test('SERVICE_ETP_V1 has an independent source boundary and never imports Retail ETP engines or dictionaries',()=>{
  for(const file of ['etp-e7-authority-intake.js','etp-e7-service-verifier.js','etp-e7-service-operational.js']){
    const source=fs.readFileSync(new URL('../www/'+file,import.meta.url),'utf8');
    assert.doesNotMatch(source,/SaagarEtp(?:Import|Schema|Cro|Target|Exception|Incentive)|SaagarEtpOperationalStore|R022|WLMHW|HEMW/);
  }
  const retailScope='WLMHW|2026-27|2026-08-01..2026-08-31';
  assert.equal(verifier.verify({scopeKey:retailScope,binding:binding('7','service-receipt-001',retailScope),profile:activeProfile(),reports:{}}).code,'E7_SCOPE_INVALID');
  assert.equal(make().operational.load({scopeKey:retailScope}).code,'E7_SCOPE_INVALID');
});

test('custody, consent and retention remain separate mandatory Owner-approved authority, never defaults',()=>{
  const complete=approvedPackage();
  delete complete.SERVICE_CUSTODY_CONSENT_RETENTION_AUTHORITY;
  assert.equal(authority.readiness(complete).code,'E7_AUTHORITY_DEFERRED');
  for(const field of ['custodyRule','consentRule','retentionRule','custodyCompletenessScope']){
    const c=candidate('SERVICE_CUSTODY_CONSENT_RETENTION_AUTHORITY');
    c.payload[field]=null;
    assert.equal(authority.prepareCandidate(c).code,'E7_SOURCE_SCHEMA_INVALID',field);
  }
  const c=candidate('SERVICE_CUSTODY_CONSENT_RETENTION_AUTHORITY');
  const owner=approval('SERVICE_CUSTODY_CONSENT_RETENTION_AUTHORITY',c);
  assert.equal(authority.validateApprovedCandidate(c,{...owner,approvedByRole:'Store Manager'}).code,'E7_SOURCE_OWNER_APPROVAL_INVALID');
});

test('exact S003/S004 identities and exact keys reject aliases, widened rows, PII and prototype payloads',()=>{
  const base={scopeKey,binding:binding(),profile:activeProfile(),reports:reports()};
  for(const mutate of [
    r=>{r.S003_REVENUE.reportType='S003';},
    r=>{r.S004_TENDER_DETAILED.headerSignatureSha256='f'.repeat(64);},
    r=>{r.S003_REVENUE.rows[0].customerName='Sensitive Person';},
    r=>{r.S004_TENDER_DETAILED.rows[0].phone='9999999999';},
    r=>{r.S003_REVENUE.rows[0].constructor={polluted:true};},
    r=>{r.S004_TENDER_DETAILED.rows[0].jobKey='__proto__';}
  ]){
    const hostile=structuredClone(base.reports);mutate(hostile);
    assert.equal(verifier.verify({...base,reports:hostile}).ok,false);
  }
  assert.equal({}.polluted,undefined);
});

test('scope, generation, receipt and authority drift all fail closed without persisting a second history',()=>{
  const made=make(),first=made.operational.verify(verifyRequest());
  assert.equal(first.ok,true);const before=made.storage.value();
  const crossScope=scopeKey.replace('SC01','SC02');
  assert.equal(made.operational.verify(verifyRequest({scopeKey:crossScope,operationId:'cross-store'})).code,'E7_AUTHORITY_DEFERRED');
  made.setBinding(binding('8','service-receipt-002'));
  assert.equal(made.operational.load({scopeKey}).code,'E7_VERIFIED_BINDING_DRIFT');
  assert.equal(made.storage.value(),before);
  made.setBinding(binding());
  const drift=structuredClone(activeProfile());drift.sources.SERVICE_REPORT_IDENTITY_SET.approvalId='OWNER-RESTATED-02';made.setProfile(drift);
  assert.equal(made.operational.load({scopeKey}).code,'E7_AUTHORITY_REAPPROVAL_REQUIRED');
  assert.equal(made.storage.value(),before);
});

test('append-only evidence and closure keep the immutable verification lineage and exclude source rows',()=>{
  const made=make(),verified=made.operational.verify(verifyRequest());
  const id=verified.run.result.discrepancies[0].discrepancyId;
  const evidence=made.operational.addEvidence({scopeKey,actorId:'manager-1',actorRole:'Store Manager',reauthenticatedAt:now,eventId:'evidence-001',runOperationId:'verify-001',discrepancyId:id,evidenceKind:'SERVICE_RECEIPT',evidenceReference:'receipt-proof-001',note:null});
  assert.equal(evidence.ok,true);
  const closed=made.operational.close({scopeKey,actorId:'owner-1',actorRole:'Owner',reauthenticatedAt:now,eventId:'closure-001',runOperationId:'verify-001',discrepancyId:id,closureReasonCode:'SOURCE_CONFIRMED',closureNote:null,evidenceReference:'receipt-proof-001'});
  assert.equal(closed.ok,true);
  assert.deepEqual(closed.state.audit.map(x=>[x.sequence,x.event]),[[1,'VERIFIED'],[2,'EVIDENCE_ADDED'],[3,'CLOSED']]);
  assert.equal(closed.state.runs[0].result.discrepancies[0].status,'OPEN');
  assert.deepEqual(closed.state.runs[0],verified.run);
  assert.doesNotMatch(made.storage.value(),/customer|mobile|phone|email|address|aadhaar|\"rows\"|transactionValue|paymentValue/i);
});

test('portable service evidence remains isolated, corrupt/hostile restore payloads fail atomically, and exact reimport is mandatory',()=>{
  const source=make();source.operational.verify(verifyRequest());
  const backup=source.repository.exportPortable('2026-08-25T11:00:00Z').backup;
  assert.doesNotMatch(JSON.stringify(backup),/customer|mobile|phone|email|address|aadhaar|\"rows\"/i);
  const target=make(),before=target.storage.value();
  const hostile=structuredClone(backup);hostile.items[0].state.__protoPollution='blocked';
  assert.equal(target.repository.restorePortable(hostile).code,'E7_PORTABLE_RESTORE_INVALID');
  assert.equal(target.storage.value(),before);
  assert.equal(target.repository.restorePortable(backup).ok,true);
  assert.equal(target.operational.load({scopeKey}).code,'E7_RESTORE_REIMPORT_REQUIRED');
  assert.equal(target.repository.rebindVerifiedScope(binding('7','wrong-receipt')).code,'E7_REBIND_EXACT_MATCH_REQUIRED');
  assert.equal(target.repository.rebindVerifiedScope(binding()).ok,true);
  assert.equal(target.operational.load({scopeKey}).status,'READY');
});

test('authority intake rejects hostile schemas and does not manufacture external evidence or approval',()=>{
  const c=candidate('SERVICE_REPORT_IDENTITY_SET');
  Object.defineProperty(c.payload,'__proto__',{value:{polluted:true},enumerable:true});
  assert.equal(authority.prepareCandidate(c).code,'E7_SOURCE_SCHEMA_INVALID');
  const clean=candidate('SERVICE_REPORT_IDENTITY_SET'),prepared=authority.prepareCandidate(clean);
  assert.equal(prepared.status,'CANDIDATE_VALIDATED_UNAPPROVED');
  assert.equal('approval' in prepared,false);
  assert.equal('evidence' in prepared,false);
  assert.equal(intake.candidateTemplate('SERVICE_REPORT_IDENTITY_SET',scopeKey).status,'CANDIDATE_UNAPPROVED');
  assert.equal({}.polluted,undefined);
});

test('mounted Service presentation projects text-only evidence and keeps narrow/mobile layout inside its own host boundary',()=>{
  const made=make();made.operational.verify(verifyRequest());
  const loaded=structuredClone(made.operational.load({scopeKey}));
  const model=presentation.cleanModel(loaded);
  assert.equal(model.boundary,'SERVICE_ETP_V1');
  assert.doesNotMatch(JSON.stringify(model),/customer|mobile|phone|email|address|aadhaar|<img/i);
  loaded.customerName='<img src=x onerror=alert(1)>';
  loaded.state.runs[0].result.customerMobile='9999999999';
  assert.equal(presentation.cleanModel(loaded),null);
  const uiSource=fs.readFileSync(new URL('../www/etp-e7-presentation.js',import.meta.url),'utf8');
  const hostSource=fs.readFileSync(new URL('../www/etp-e7-module-host.js',import.meta.url),'utf8');
  const css=fs.readFileSync(new URL('../www/etp-e7-presentation.css',import.meta.url),'utf8');
  assert.doesNotMatch(uiSource,/innerHTML|insertAdjacentHTML|SaagarEtp(?:OperationalBootstrap|Cro|Target|Exception|Incentive)/);
  assert.match(hostSource,/querySelector\('\[data-etp-operational-e7\]'\)/);
  assert.doesNotMatch(hostSource,/data-etp-operational-(?:e3|e4|e5|e6)|SaagarEtpOperationalBootstrap/);
  assert.match(css,/@media\(max-width:480px\)/);
  assert.match(css,/max-width:100%/);
  assert.match(css,/grid-template-columns:1fr/);
  assert.match(css,/overflow-wrap:anywhere/);
  assert.doesNotMatch(css,/(?:^|[;{])(?:min-)?width:\s*[5-9]\d\dpx/m);
});
