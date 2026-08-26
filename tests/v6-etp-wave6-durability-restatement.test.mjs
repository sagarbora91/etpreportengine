import test from 'node:test';
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';

const require=createRequire(import.meta.url);
const foundation=require('../www/etp-operational-foundation.js');
const storeApi=require('../www/etp-operational-store.js');
const runtimeApi=require('../www/etp-operational-runtime.js');
const adapters=require('../www/etp-operational-adapters.js');
const gatewayApi=require('../www/etp-operational-gateway.js');
const e3Orchestrator=require('../www/etp-e3-orchestrator.js');
const e4Orchestrator=require('../www/etp-e4-orchestrator.js');
const e3Engine=require('../www/etp-cro-reconciliation.js');
const e4Engine=require('../www/etp-target-planning.js');

const scopeKey='WLMHW|2026-27|2026-08-01..2026-08-31';
const day='2026-08-24';
const bindingA={scopeKey,generationId:'etp_'+'a'.repeat(32),receiptId:'receipt-a'};
const bindingB={scopeKey,generationId:'etp_'+'b'.repeat(32),receiptId:'receipt-b'};
const now='2026-08-25T10:00:00.000Z';

function memory(seed={}){
  const map=new Map(Object.entries(seed));
  return {map,getItem:key=>map.has(key)?map.get(key):null,setItem:(key,value)=>map.set(key,String(value)),removeItem:key=>map.delete(key)};
}

function authority(domain){
  return foundation.registerAuthority({domain,status:'ACTIVE',sourceSha256:(domain==='E3'?'a':'b').repeat(64),approvalId:domain+'-OWNER-WAVE6',approvedAt:'2026-08-25T08:00:00.000Z',approvedByRole:'Owner',stores:['WLMHW']});
}

function runtime(storage){
  const made=runtimeApi.create({storage,storeApi,foundation});
  assert.equal(made.ok,true,made.code);
  return made.runtime;
}

function gateway(repository,readE3=(request)=>({
  source:'ETP_VERIFIED',scopeKey:request.scopeKey,generationId:request.generationId,receiptId:request.receiptId,
  rows:[{invoiceId:'INV-1',storeId:'WLMHW',invoiceDate:day,netAmount:100,croId:'CRO-1'}]
})){
  const made=gatewayApi.create({
    foundation,operationalStore:repository,adapters,e3Orchestrator,e4Orchestrator,e3Engine,e4Engine,
    verifiedFacade:{readE3},authorityProvider:domain=>authority(domain),clock:()=>now
  });
  assert.equal(made.ok,true,made.code);
  return made.gateway;
}

function request(binding=bindingA,role='Store Manager'){
  return {scopeKey,binding,businessDate:day,actorId:'ACTOR-1',actorRole:role,at:now,reauthenticatedAt:role==='Staff'?null:now,reason:'wave 6 governed action'};
}

function getE3(repository){
  return repository.get({domain:'E3',storeCode:'WLMHW',financialYear:'2026-27',scopeKey,overlayId:'E3-'+day});
}

async function lockMatchedDay(g){
  assert.equal((await g.E3.open(request())).ok,true);
  assert.equal((await g.E3.declare({...request(bindingA,'Staff'),declaration:{invoiceId:'INV-1',croId:'CRO-1',netAmount:100}})).ok,true);
  assert.equal((await g.E3.close(request())).ok,true);
  assert.equal((await g.E3.importVerified(request())).ok,true);
  assert.equal((await g.E3.state({...request(),to:'IMPORTED'})).ok,true);
  assert.equal((await g.E3.reconcile(request())).ok,true);
  assert.equal((await g.E3.state({...request(),to:'RECONCILED'})).ok,true);
  assert.equal((await g.E3.lock(request())).ok,true);
}

test('multi-overlay restore into a non-empty target replaces atomically and remains restart-valid',()=>{
  const sourceStorage=memory(),source=runtime(sourceStorage),repository=source.repository;
  const base={storeCode:'WLMHW',financialYear:'2026-27',scopeKey,updatedAt:now};
  assert.equal(repository.put({...base,domain:'E3',overlayId:'E3-'+day,payload:{kind:'HUMAN_ACTION',state:'LOCKED'}},0).ok,true);
  assert.equal(repository.put({...base,domain:'E6',overlayId:'E6-CASE-1',payload:{kind:'HUMAN_ACTION',owner:'OWNER-1'}},0).ok,true);
  const backup=source.exportPortable('2026-08-25T11:00:00.000Z').backup;

  const targetStorage=memory(),target=runtime(targetStorage);
  assert.equal(target.repository.put({...base,domain:'E4',overlayId:'E4-OLD',payload:{kind:'OLD_TARGET'}},0).ok,true);
  assert.equal(target.importPortable(backup,'2026-08-25T12:00:00.000Z').ok,true);
  assert.deepEqual(target.repository.history().events.map(event=>[event.sequence,event.type]),[[1,'RESTORED'],[2,'RESTORED']]);
  assert.equal(target.repository.list({domain:'E4',storeCode:'WLMHW',financialYear:'2026-27',scopeKey}).items.length,0);

  const restarted=runtime(targetStorage);
  assert.equal(restarted.repository.list({domain:'E3',storeCode:'WLMHW',financialYear:'2026-27',scopeKey}).items.length,1);
  assert.equal(restarted.repository.list({domain:'E6',storeCode:'WLMHW',financialYear:'2026-27',scopeKey}).items.length,1);
  assert.deepEqual(restarted.repository.history().events.map(event=>event.sequence),[1,2]);
});

test('locked E3 survives restart; portable restore preserves human action and fences verified reads until exact rebind',async()=>{
  const sourceStorage=memory(),source=runtime(sourceStorage),sourceGateway=gateway(source.repository);
  await lockMatchedDay(sourceGateway);
  const locked=getE3(source.repository);
  assert.equal(locked.found,true);
  const lockedRecord=locked.overlay.payload.record.day;

  const restartedSource=runtime(sourceStorage),restartedGateway=gateway(restartedSource.repository);
  assert.equal((await restartedGateway.E3.load(request())).day.state,'LOCKED');
  assert.equal((await restartedGateway.E3.correct({...request(bindingA,'Owner'),correction:{invoiceId:'INV-1',croId:'CRO-2'}})).code,'DAY_LOCKED');
  assert.equal(getE3(restartedSource.repository).overlay.payload.record.day,lockedRecord);

  const backup=restartedSource.exportPortable('2026-08-25T11:00:00.000Z').backup;
  const targetStorage=memory(),target=runtime(targetStorage);
  assert.equal(target.importPortable(backup,'2026-08-25T12:00:00.000Z').ok,true);
  const restoredGateway=gateway(target.repository);
  const restored=await restoredGateway.E3.load(request());
  assert.equal(restored.day.state,'LOCKED');
  assert.equal(restored.day.declarations[0].invoiceId,'INV-1');
  assert.equal((await restoredGateway.E3.restatement({...request(),nextBinding:bindingB})).code,'E3_RESTORE_REIMPORT_REQUIRED');
  assert.equal(target.rebindVerifiedScope({source:'ETP_VERIFIED',...bindingA}).ok,true);
  assert.equal(target.canReadVerifiedScope(scopeKey,{source:'ETP_VERIFIED',...bindingA}),true);
  assert.equal(target.canReadVerifiedScope(scopeKey,{source:'ETP_VERIFIED',...bindingA,receiptId:'receipt-drift'}),false);
  const rebound=runtime(targetStorage);
  assert.equal(rebound.canReadVerifiedScope(scopeKey,{source:'ETP_VERIFIED',...bindingA}),true);
});

test('source restatement requires Owner and a genuinely new verified generation, then persists a new audited cycle',async()=>{
  const storage=memory(),active=runtime(storage),g=gateway(active.repository);
  await lockMatchedDay(g);
  const lockedRecord=getE3(active.repository).overlay.payload.record.day;

  assert.equal((await g.E3.restatement({...request(),nextBinding:bindingB})).code,'E3_FRESH_GRANT_REQUIRED');
  assert.equal((await g.E3.restatement({...request(bindingA,'Owner'),nextBinding:{...bindingA,receiptId:'receipt-new'}})).code,'E3_NEW_GENERATION_REQUIRED');

  const restated=await g.E3.restatement({...request(bindingA,'Owner'),nextBinding:bindingB});
  assert.equal(restated.ok,true);
  assert.equal(restated.cycle,2);
  assert.equal(restated.day.state,'CLOSED');
  assert.equal(restated.day.sourceBinding.generationId,bindingB.generationId);
  assert.equal(restated.day.sourceBinding.receiptId,bindingB.receiptId);
  const importedAudit=restated.day.audit.find(event=>event.event==='SOURCE_FACTS_IMPORTED');
  assert.equal(importedAudit.after.sourceBinding.generationId,bindingB.generationId);
  assert.equal(importedAudit.after.sourceBinding.receiptId,bindingB.receiptId);
  assert.equal(Object.isFrozen(restated),true);
  assert.equal(JSON.parse(lockedRecord).day.state,'LOCKED');

  const restarted=runtime(storage),loaded=await gateway(restarted.repository).E3.load(request(bindingB));
  assert.equal(loaded.cycle,2);
  assert.equal(loaded.day.state,'CLOSED');
  assert.equal(loaded.day.sourceBinding.generationId,bindingB.generationId);
});
