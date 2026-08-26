import test from 'node:test';
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';

const require=createRequire(import.meta.url);
const foundation=require('../www/etp-operational-foundation.js');
const storeApi=require('../www/etp-operational-store.js');
const runtimeApi=require('../www/etp-operational-runtime.js');
const adapters=require('../www/etp-operational-adapters.js');
const engine=require('../www/etp-cro-reconciliation.js');

const scopeKey='WLMHW|2026-27|2026-08-01..2026-08-31';
const businessDate='2026-08-24';
const at='2026-08-25T10:00:00.000Z';
const actor={actor:'ACTOR-1',role:'Owner',at,reason:'portable privacy acceptance'};

function memory(){
  const map=new Map();
  return {map,getItem:key=>map.has(key)?map.get(key):null,setItem:(key,value)=>map.set(key,String(value)),removeItem:key=>map.delete(key)};
}

function runtime(storage){
  const made=runtimeApi.create({storage,storeApi,foundation});
  assert.equal(made.ok,true,made.code);
  return made.runtime;
}

function lockedRecord(){
  let out=engine.createDay({storeId:'WLMHW',businessDate});
  out=engine.declare(out.day,{invoiceId:'INV-HUMAN',croId:'CRO-DECLARED',netAmount:111},actor);
  out=engine.transition(out.day,'CLOSED',actor);
  out=engine.importFacts(out.day,{source:'ETP_VERIFIED',scopeKey,generationId:'etp_'+'a'.repeat(32),receiptId:'receipt-private',rows:[
    {invoiceId:'INV-HUMAN',storeId:'WLMHW',invoiceDate:businessDate,netAmount:111,croId:'CRO-VERIFIED-PRIVATE'},
    {invoiceId:'INV-VERIFIED-PRIVATE',storeId:'WLMHW',invoiceDate:businessDate,netAmount:222,croId:'CRO-SECRET'}
  ]},actor);
  out=engine.transition(out.day,'IMPORTED',actor);
  out=engine.reconcile(out.day,actor);
  out=engine.transition(out.day,'VARIANCE',actor);
  out=engine.correctAttribution(out.day,{invoiceId:'INV-HUMAN',croId:'CRO-CORRECTED'},actor);
  out=engine.disposeVariance(out.day,{code:'OWNER_REVIEWED',invoiceId:null},actor);
  out=engine.transition(out.day,'LOCKED',actor);
  const serialized=engine.serialize(out.day);
  assert.equal(serialized.ok,true);
  return {version:'ETP_E3_ORCHESTRATOR_V1',policyVersion:engine.POLICY_VERSION,authorityApprovalId:'E3-OWNER-WAVE6',cycle:1,day:serialized.overlay};
}

test('runtime portable export removes verified E3 facts and computed evidence but keeps human actions and source binding',()=>{
  const sourceStorage=memory(),source=runtime(sourceStorage),record=lockedRecord();
  assert.equal(source.repository.put({domain:'E3',storeCode:'WLMHW',financialYear:'2026-27',scopeKey,overlayId:'E3-'+businessDate,updatedAt:at,payload:{kind:'E3_ORCHESTRATOR_STATE',authorityApprovalId:'E3-OWNER-WAVE6',policyVersion:engine.POLICY_VERSION,orchestratorVersion:'ETP_E3_ORCHESTRATOR_V1',cycle:1,record}},0).ok,true);

  const exported=source.exportPortable('2026-08-25T11:00:00.000Z');
  assert.equal(exported.ok,true,exported.code);
  const portableRecord=exported.backup.overlays[0].payload.record;
  const day=JSON.parse(portableRecord.day).day;
  assert.equal(day.state,'LOCKED');
  assert.deepEqual(day.declarations,[{invoiceId:'INV-HUMAN',croId:'CRO-DECLARED',netAmount:111}]);
  assert.deepEqual(day.dispositions.map(x=>x.code),['OWNER_REVIEWED']);
  assert.deepEqual(day.sourceBinding,{source:'ETP_VERIFIED',scopeKey,generationId:'etp_'+'a'.repeat(32),receiptId:'receipt-private'});
  assert.deepEqual(day.sourceFacts,[]);
  assert.deepEqual(day.outcomes,[]);
  assert.deepEqual(day.unassignedQueue,[]);
  assert.deepEqual(day.assignments,{'INV-HUMAN':'CRO-CORRECTED'});
  assert.deepEqual(day.audit.map(x=>x.sequence),day.audit.map((_,i)=>i+1));
  assert.ok(day.audit.some(x=>x.event==='INVOICE_DECLARED'));
  assert.ok(day.audit.some(x=>x.event==='ATTRIBUTION_CORRECTED'&&x.before===null&&x.after.croId==='CRO-CORRECTED'));
  assert.ok(day.audit.some(x=>x.event==='VARIANCE_DISPOSED'));
  assert.ok(!day.audit.some(x=>x.event==='SOURCE_FACTS_IMPORTED'||x.event==='RECONCILIATION_COMPUTED'));
  assert.doesNotMatch(JSON.stringify(exported),/INV-VERIFIED-PRIVATE|CRO-VERIFIED-PRIVATE|CRO-SECRET/);

  const targetStorage=memory(),target=runtime(targetStorage);
  assert.equal(target.importPortable(exported.backup,'2026-08-25T12:00:00.000Z').ok,true);
  const restarted=runtime(targetStorage);
  const madeAdapter=adapters.createE3({operationalStore:restarted.repository,scopeKey,now:()=>at});
  assert.equal(madeAdapter.ok,true,madeAdapter.code);
  return madeAdapter.adapter.load('E3|WLMHW|'+businessDate).then(restored=>{
    const restoredDay=engine.restore(restored.day);
    assert.equal(restoredDay.ok,true,restoredDay.code);
    assert.equal(restoredDay.day.state,'LOCKED');
    assert.deepEqual(restoredDay.day.declarations,day.declarations);
    assert.deepEqual(restoredDay.day.dispositions,day.dispositions);
    assert.deepEqual(restoredDay.day.sourceBinding,day.sourceBinding);
  });
});

test('malformed serialized E3 operational records fail closed instead of exporting uninspected content',()=>{
  const source=runtime(memory());
  assert.equal(source.repository.put({domain:'E3',storeCode:'WLMHW',financialYear:'2026-27',scopeKey,overlayId:'E3-'+businessDate,updatedAt:at,payload:{kind:'E3_ORCHESTRATOR_STATE',record:{day:'not-json'}}},0).ok,true);
  assert.equal(source.exportPortable('2026-08-25T11:00:00.000Z').code,'ETP_STORE_PORTABLE_INVALID');
});
