import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {createRequire} from 'node:module';
import {scopeKey,approvedPackage,api as authority} from './lib/v6-etp-e7-fixtures.mjs';

const require=createRequire(import.meta.url);
const operationalApi=require('../www/etp-e7-service-operational.js');
const verifier=require('../www/etp-e7-service-verifier.js');
const profile=()=>authority.activateApprovedPackage(approvedPackage()).profile;
const now='2026-08-25T10:00:00.000Z';
const binding=(digit='7',receiptId='service-receipt-001')=>({source:'ETP_VERIFIED',boundary:'SERVICE_ETP_V1',scopeKey,generationId:'etp_'+digit.repeat(32),receiptId});
const memoryStorage=(seed=null)=>{let value=seed,writes=0;return{getItem:()=>value,setItem:(_key,next)=>{value=next;writes++;},value:()=>value,writes:()=>writes};};
const reports=(b=binding())=>{const p=profile(),hashes=Object.fromEntries(p.reportIdentities.map(x=>[x.reportType,x.headerSignatureSha256])),report=(reportType,rows)=>({reportType,scopeKey,generationId:b.generationId,receiptId:b.receiptId,headerSignatureSha256:hashes[reportType],rows});return{
  S003_REVENUE:report('S003_REVENUE',[{jobKey:'JOB-001',transactionValue:'Service Revenue',amountPaise:10000,evidenceId:'s003-row-001'},{jobKey:'JOB-002',transactionValue:'Part Revenue',amountPaise:25000,evidenceId:'s003-row-002'}]),
  S004_TENDER_DETAILED:report('S004_TENDER_DETAILED',[{jobKey:'JOB-001',paymentValue:'Cash',amountPaise:10000,evidenceId:'s004-row-001'},{jobKey:'JOB-002',paymentValue:'UPI',amountPaise:24000,evidenceId:'s004-row-002'}])
};};
const make=(storage=memoryStorage(),overrides={})=>{let activeBinding=Object.prototype.hasOwnProperty.call(overrides,'binding')?overrides.binding:binding(),activeProfile=overrides.profile===undefined?profile():overrides.profile,reauth=overrides.reauth===undefined?true:overrides.reauth,authorized=overrides.authorized===undefined?true:overrides.authorized;const made=operationalApi.create({storage,storageKey:'saagar.e7.service.v1',verifier,authorityProvider:()=>activeProfile,bindingProvider:()=>activeBinding,now:()=>now,reauth:()=>reauth,authorize:()=>authorized});return{...made,storage,setBinding:v=>{activeBinding=v;},setProfile:v=>{activeProfile=v;},setReauth:v=>{reauth=v;},setAuthorized:v=>{authorized=v;}};};
const verifyRequest=(b=binding(),extra={})=>({scopeKey,actorId:'owner-1',actorRole:'Owner',operationId:'verify-001',reports:reports(b),reauthenticatedAt:now,...extra});

test('E7 remains honestly BLOCKED and writes nothing when authority or verified source is absent',()=>{
  const noAuthority=make(memoryStorage(),{profile:null});
  assert.equal(noAuthority.operational.load({scopeKey}).code,'E7_AUTHORITY_DEFERRED');
  assert.equal(noAuthority.storage.writes(),0);
  const noSource=make(memoryStorage(),{binding:null});
  assert.equal(noSource.operational.load({scopeKey}).code,'E7_VERIFIED_SOURCE_UNAVAILABLE');
  assert.equal(noSource.storage.writes(),0);
});

test('S003/S004 verification persists only the PII-free projection across a full restart',()=>{
  const storage=memoryStorage(),first=make(storage),out=first.operational.verify(verifyRequest());
  assert.equal(out.ok,true);assert.equal(out.changed,true);assert.equal(out.run.result.status,'VARIANCE');
  assert.equal(out.run.result.discrepancies[0].discrepancyId,'verify-001|JOB-002|AMOUNT_VARIANCE');
  assert.equal(out.state.runs.length,1);assert.equal(out.state.activeBinding.boundary,'SERVICE_ETP_V1');
  const serialized=storage.value();
  assert.doesNotMatch(serialized,/transactionValue|paymentValue|Service Revenue|customerMobile|rawrows/i);
  const restarted=make(memoryStorage(serialized)),loaded=restarted.operational.load({scopeKey});
  assert.equal(loaded.status,'READY');assert.deepEqual(loaded.state,out.state);
  const replay=restarted.operational.verify(verifyRequest());assert.equal(replay.changed,false);assert.equal(replay.state.runs.length,1);
});

test('retail scopes, caller-supplied authority and caller-supplied bindings cannot cross the service boundary',()=>{
  const made=make();
  assert.equal(made.operational.load({scopeKey:'WLMHW|2026-27|2026-08-01..2026-08-31'}).code,'E7_SCOPE_INVALID');
  const widened={...verifyRequest(),binding:binding('8'),profile:null};
  assert.equal(made.operational.verify(widened).ok,true);
  assert.equal(made.operational.load({scopeKey}).binding.generationId,binding().generationId);
});

test('authority drift rejects every read and mutation until separately reapproved',()=>{
  const made=make();made.operational.verify(verifyRequest());
  const drift=structuredClone(profile());drift.sources.SERVICE_PAYMENT_DICTIONARY.sourceSha256='f'.repeat(64);made.setProfile(drift);
  assert.equal(made.operational.load({scopeKey}).code,'E7_AUTHORITY_REAPPROVAL_REQUIRED');
  assert.equal(made.operational.verify({...verifyRequest(),operationId:'verify-002'}).code,'E7_AUTHORITY_REAPPROVAL_REQUIRED');
});

test('source restatement appends a new immutable run while ordinary reads reject binding drift',()=>{
  const made=make(),first=made.operational.verify(verifyRequest()),next=binding('8','service-receipt-002');assert.equal(first.ok,true);
  made.setBinding(next);assert.equal(made.operational.load({scopeKey}).code,'E7_VERIFIED_BINDING_DRIFT');
  const second=made.operational.verify(verifyRequest(next,{operationId:'verify-002'}));
  assert.equal(second.ok,true);assert.equal(second.state.runs.length,2);
  assert.equal(second.state.runs[0].binding.generationId,binding().generationId);
  assert.equal(second.state.activeBinding.generationId,next.generationId);
  assert.equal(made.operational.load({scopeKey}).status,'READY');
});

test('evidence and closure are fresh-reauthenticated append-only events and closed discrepancies are immutable',()=>{
  const made=make(),verified=made.operational.verify(verifyRequest()),id=verified.run.result.discrepancies[0].discrepancyId;
  made.setReauth(false);assert.equal(made.operational.addEvidence({scopeKey,actorId:'owner-1',actorRole:'Owner',reauthenticatedAt:now,eventId:'evidence-001',runOperationId:'verify-001',discrepancyId:id,evidenceKind:'SERVICE_RECEIPT',evidenceReference:'receipt-proof-001',note:null}).code,'E7_FRESH_GRANT_REQUIRED');
  made.setReauth(true);const evidence=made.operational.addEvidence({scopeKey,actorId:'manager-1',actorRole:'Store Manager',reauthenticatedAt:now,eventId:'evidence-001',runOperationId:'verify-001',discrepancyId:id,evidenceKind:'SERVICE_RECEIPT',evidenceReference:'receipt-proof-001',note:null});assert.equal(evidence.ok,true);
  const closed=made.operational.close({scopeKey,actorId:'owner-1',actorRole:'Owner',reauthenticatedAt:now,eventId:'closure-001',runOperationId:'verify-001',discrepancyId:id,closureReasonCode:'SOURCE_CONFIRMED',closureNote:null,evidenceReference:'receipt-proof-001'});assert.equal(closed.ok,true);assert.equal(closed.state.evidence.length,1);assert.equal(closed.state.closures.length,1);
  const duplicate=made.operational.close({scopeKey,actorId:'owner-1',actorRole:'Owner',reauthenticatedAt:now,eventId:'closure-002',runOperationId:'verify-001',discrepancyId:id,closureReasonCode:'SOURCE_CONFIRMED',closureNote:null,evidenceReference:'receipt-proof-001'});assert.equal(duplicate.code,'E7_DISCREPANCY_ALREADY_CLOSED');
  assert.equal(made.operational.load({scopeKey}).state.closures[0].eventId,'closure-001');
});

test('mutation permission is injected from active service authority and never inferred from a role label',()=>{
  const made=make(memoryStorage(),{authorized:false});
  assert.equal(made.operational.verify(verifyRequest()).code,'E7_ACTION_NOT_AUTHORIZED');
  assert.equal(made.storage.writes(),0);
  made.setAuthorized(true);assert.equal(made.operational.verify(verifyRequest()).ok,true);
  made.setAuthorized(false);const state=made.operational.load({scopeKey}).state,id=state.runs[0].result.discrepancies[0].discrepancyId;
  assert.equal(made.operational.close({scopeKey,actorId:'owner-1',actorRole:'Owner',reauthenticatedAt:now,eventId:'closure-001',runOperationId:'verify-001',discrepancyId:id,closureReasonCode:'SOURCE_CONFIRMED',closureNote:null,evidenceReference:'receipt-proof-001'}).code,'E7_ACTION_NOT_AUTHORIZED');
});

test('repository itself rejects deletion or rewriting of verified runs, evidence and closure history',()=>{
  const made=make(),verified=made.operational.verify(verifyRequest()),id=verified.run.result.discrepancies[0].discrepancyId;
  made.operational.addEvidence({scopeKey,actorId:'owner-1',actorRole:'Owner',reauthenticatedAt:now,eventId:'evidence-001',runOperationId:'verify-001',discrepancyId:id,evidenceKind:'SERVICE_RECEIPT',evidenceReference:'receipt-proof-001',note:null});
  const loaded=made.repository.load(scopeKey),rewritten=structuredClone(loaded.state);rewritten.runs[0].result.discrepancies[0].variancePaise=999;
  assert.equal(made.repository.save(scopeKey,rewritten,loaded.revision).code,'E7_REPOSITORY_IMMUTABLE_HISTORY');
  const deleted=structuredClone(loaded.state);deleted.evidence=[];deleted.audit=deleted.audit.slice(0,1);
  assert.equal(made.repository.save(scopeKey,deleted,loaded.revision).code,'E7_REPOSITORY_IMMUTABLE_HISTORY');
  assert.deepEqual(made.repository.load(scopeKey),loaded);
});

test('portable restore is fenced and only the exact verified generation/receipt can rebind it',()=>{
  const source=make();source.operational.verify(verifyRequest());const backup=source.repository.exportPortable('2026-08-25T11:00:00Z').backup;
  const target=make(),restored=target.repository.restorePortable(backup);assert.equal(restored.ok,true);
  assert.equal(target.operational.load({scopeKey}).code,'E7_RESTORE_REIMPORT_REQUIRED');
  assert.equal(target.repository.rebindVerifiedScope(binding('7','wrong-receipt')).code,'E7_REBIND_EXACT_MATCH_REQUIRED');
  assert.equal(target.repository.rebindVerifiedScope(binding()).ok,true);
  assert.equal(target.operational.load({scopeKey}).status,'READY');
  target.setBinding(binding('8','service-receipt-002'));
  assert.equal(target.operational.load({scopeKey}).code,'E7_RESTORE_REIMPORT_REQUIRED');
});

test('failed storage writes are atomic and corrupt restart payloads fail closed',()=>{
  let raw=null;const storage={getItem:()=>raw,setItem:()=>{throw new Error('disk full');}},made=make(storage);
  assert.equal(made.operational.verify(verifyRequest()).code,'E7_REPOSITORY_WRITE_FAILED');assert.equal(raw,null);
  assert.equal(operationalApi.createRepository({storage:memoryStorage('{"contractVersion":"bad"}'),storageKey:'saagar.e7.service.v1'}).code,'E7_REPOSITORY_CORRUPT');
});

test('module has no retail repository dependency, raw-source persistence, browser globals or modern-only syntax',()=>{
  const source=fs.readFileSync(new URL('../www/etp-e7-service-operational.js',import.meta.url),'utf8');
  assert.doesNotMatch(source,/SaagarEtpOperationalStore|WLMHW|HEMW|localStorage|sessionStorage|indexedDB|document\.|window\.|fetch\s*\(|XMLHttpRequest|async\s*\*|\?\.|\?\?/);
});
