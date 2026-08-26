import test from 'node:test';
import assert from 'node:assert/strict';
import intake from '../www/etp-e7-authority-intake.js';
import {scopeKey,api,candidate,approval,approvedPackage} from './lib/v6-etp-e7-fixtures.mjs';

test('E7 templates are explicitly unapproved and enumerate every independent Service authority',()=>{
  assert.deepEqual(intake.SOURCE_TYPES,['SERVICE_REPORT_IDENTITY_SET','SERVICE_JOB_STATUS_DICTIONARY','SERVICE_TRANSACTION_DICTIONARY','SERVICE_PAYMENT_DICTIONARY','SERVICE_SKU_TOKEN_DICTIONARY','SERVICE_CUSTODY_CONSENT_RETENTION_AUTHORITY']);
  for(const type of intake.SOURCE_TYPES){const made=intake.candidateTemplate(type,scopeKey);assert.equal(made.status,'CANDIDATE_UNAPPROVED');assert.equal(api.prepareCandidate(made.candidate).code,'E7_SOURCE_SCHEMA_INVALID');assert.equal('approval' in made.candidate,false);}
});

test('candidate normalization is deterministic, hash-bound and requires separate Owner approval',()=>{
  const c=candidate('SERVICE_REPORT_IDENTITY_SET'),prepared=api.prepareCandidate(c);
  assert.equal(prepared.status,'CANDIDATE_VALIDATED_UNAPPROVED');
  assert.equal(prepared.sourceSha256.length,64);
  assert.equal(api.prepareCandidate({payload:c.payload,scopeKey:c.scopeKey,sourceType:c.sourceType,contractVersion:c.contractVersion}).sourceSha256,prepared.sourceSha256);
  assert.equal(api.validateApprovedCandidate(c,approval('SERVICE_REPORT_IDENTITY_SET',c)).status,'APPROVED_HASH_BOUND');
});

test('mandatory S003/S004 exact identities cannot be omitted, relabelled or made optional',()=>{
  for(const mutate of [
    p=>p.identities.shift(),
    p=>{p.identities[0].required=false;},
    p=>{p.identities[0].reportType='RETAIL_R022';},
    p=>{p.identities[0].headerSignatureSha256='0'.repeat(63);}
  ]){const c=candidate('SERVICE_REPORT_IDENTITY_SET');mutate(c.payload);assert.equal(api.prepareCandidate(c).code,'E7_SOURCE_SCHEMA_INVALID');}
});

test('widened schemas, duplicate dictionary keys, cross-scope and non-Owner approval fail closed',()=>{
  const widened=candidate('SERVICE_PAYMENT_DICTIONARY');widened.payload.entries[0].guess=true;assert.equal(api.prepareCandidate(widened).code,'E7_SOURCE_SCHEMA_INVALID');
  const duplicate=candidate('SERVICE_PAYMENT_DICTIONARY');duplicate.payload.entries.push({...duplicate.payload.entries[0]});assert.equal(api.prepareCandidate(duplicate).code,'E7_SOURCE_SCHEMA_INVALID');
  const c=candidate('SERVICE_PAYMENT_DICTIONARY'),a=approval('SERVICE_PAYMENT_DICTIONARY',c);
  assert.equal(api.validateApprovedCandidate(c,{...a,approvedByRole:'Store Manager'}).code,'E7_SOURCE_OWNER_APPROVAL_INVALID');
  assert.equal(api.validateApprovedCandidate(c,{...a,scopeKey:scopeKey.replace('SC01','SC02')}).code,'E7_SOURCE_OWNER_APPROVAL_INVALID');
  assert.equal(api.validateApprovedCandidate(c,{...a,serviceUnitId:'SC02'}).code,'E7_SOURCE_OWNER_APPROVAL_INVALID');
});

test('readiness honestly remains deferred until the complete package is approved',()=>{
  const partial={SERVICE_REPORT_IDENTITY_SET:approvedPackage().SERVICE_REPORT_IDENTITY_SET};
  const blocked=api.readiness(partial);
  assert.equal(blocked.code,'E7_AUTHORITY_DEFERRED');
  assert.deepEqual(blocked.requirements.map(x=>x.status),['APPROVED','MISSING','MISSING','MISSING','MISSING','MISSING']);
  const active=api.activateApprovedPackage(approvedPackage());
  assert.equal(active.status,'ACTIVE_HASH_BOUND');
  assert.equal(active.profile.contractVersion,'SERVICE_ETP_V1');
  assert.equal(active.profile.scopeKey,scopeKey);
  assert.equal(Object.isFrozen(active.profile),true);
});

test('forged package markers, duplicated source hashes and extra package keys cannot activate E7',()=>{
  const forged=approvedPackage();forged.SERVICE_PAYMENT_DICTIONARY={...forged.SERVICE_PAYMENT_DICTIONARY,sourceSha256:'f'.repeat(64)};assert.equal(api.activateApprovedPackage(forged).code,'E7_AUTHORITY_PACKAGE_INVALID');
  const duplicate=approvedPackage();duplicate.SERVICE_PAYMENT_DICTIONARY={...duplicate.SERVICE_PAYMENT_DICTIONARY,candidate:duplicate.SERVICE_TRANSACTION_DICTIONARY.candidate,approval:duplicate.SERVICE_TRANSACTION_DICTIONARY.approval,sourceSha256:duplicate.SERVICE_TRANSACTION_DICTIONARY.sourceSha256};assert.equal(api.activateApprovedPackage(duplicate).code,'E7_AUTHORITY_PACKAGE_INVALID');
  const extra=approvedPackage();extra.RETAIL_ETP_PROFILE=extra.SERVICE_REPORT_IDENTITY_SET;assert.equal(api.activateApprovedPackage(extra).code,'E7_AUTHORITY_PACKAGE_INVALID');
});

test('policy refuses invented defaults and profile artifacts contain no customer PII fields',()=>{
  const c=candidate('SERVICE_CUSTODY_CONSENT_RETENTION_AUTHORITY');c.payload.sparsePeriodPolicy=null;assert.equal(api.prepareCandidate(c).code,'E7_SOURCE_SCHEMA_INVALID');
  const active=api.activateApprovedPackage(approvedPackage()),keys=new Set();
  const walk=v=>{if(Array.isArray(v))return v.forEach(walk);if(v&&typeof v==='object')for(const [key,value] of Object.entries(v)){keys.add(key.toLowerCase());walk(value);}};walk(active.profile);
  for(const forbidden of ['customer','customername','mobile','phone','email','address','pan','aadhaar'])assert.equal(keys.has(forbidden),false);
});
