import test from 'node:test';
import assert from 'node:assert/strict';

import foundation from '../www/etp-operational-foundation.js';
import storeApi from '../www/etp-operational-store.js';
import adapters from '../www/etp-operational-adapters.js';
import runtime from '../www/etp-operational-runtime.js';
import engine from '../www/etp-cro-reconciliation.js';
import e3Orchestrator from '../www/etp-e3-orchestrator.js';
import e4Engine from '../www/etp-target-planning.js';
import e4Orchestrator from '../www/etp-e4-orchestrator.js';
import gateway from '../www/etp-operational-gateway.js';
import bootstrap from '../www/etp-operational-bootstrap.js';

const scopeKey='WLMHW|2026-27|2026-08-01..2026-08-31';
const binding={scopeKey,generationId:`etp_${'a'.repeat(32)}`,receiptId:'receipt-wave6-live'};
const authority={domain:'E3',status:'ACTIVE',sourceSha256:'b'.repeat(64),approvalId:'E3-WAVE6-OWNER-001',approvedAt:'2026-08-25T07:00:00Z',approvedByRole:'Owner',stores:['WLMHW']};

class MemoryStorage {
  constructor(){this.values=new Map();}
  getItem(key){return this.values.has(key)?this.values.get(key):null;}
  setItem(key,value){this.values.set(key,String(value));}
  removeItem(key){this.values.delete(key);}
}

function acceptanceHarness(){
  const storage=new MemoryStorage();
  let principal={actorId:'staff-1',role:'Staff',storeCode:'WLMHW'};
  let now='2026-08-25T08:00:00.000Z';
  let reauthAt=null;
  let failVerifiedRead=false;
  let oneUseReauth=false,reauthCalls=0;
  const factsByDate={
    '2026-08-25':[
      {invoiceId:'INV-MATCH',storeId:'WLMHW',invoiceDate:'2026-08-25',netAmount:100,croId:'CRO-1'},
      {invoiceId:'INV-UNCLAIMED',storeId:'WLMHW',invoiceDate:'2026-08-25',netAmount:200,croId:null}
    ],
    '2026-08-26':[
      {invoiceId:'INV-CLEAN',storeId:'WLMHW',invoiceDate:'2026-08-26',netAmount:300,croId:'CRO-2'}
    ]
  };
  const verifiedJoin={
    getBinding:key=>key===scopeKey?binding:null,
    getNextBinding:()=>null,
    readE3:request=>{
      if(failVerifiedRead)throw new Error('verified source unavailable');
      return {source:'ETP_VERIFIED',scopeKey:request.scopeKey,generationId:request.generationId,receiptId:request.receiptId,rows:factsByDate[request.businessDate]||[]};
    }
  };
  function options(){return {
    foundation,store:storeApi,runtime,adapters,e3Engine:engine,e4Engine,e3Orchestrator,e4Orchestrator,gateway,verifiedJoin,storage,
    ownerSession:()=>principal,reauth:()=>{reauthCalls+=1;return !oneUseReauth||reauthCalls===1?(reauthAt||now):null;},clock:()=>now,authorities:{E3:authority}
  };}
  function api(){const made=bootstrap.create(options());assert.equal(made.ok,true);return made.operational.E3;}
  return {
    api:api(),
    restart:api,
    setRole(role){principal={actorId:role==='Owner'?'owner-1':role==='Store Manager'?'manager-1':'staff-1',role,storeCode:'WLMHW'};},
    setTime(value){now=value;},
    setReauth(value){reauthAt=value;},
    failVerifiedRead(value){failVerifiedRead=value;},
    useOneReauth(){oneUseReauth=true;reauthCalls=0;},
    reauthCalls(){return reauthCalls;}
  };
}

const day=businessDate=>({scopeKey,businessDate});

test('real mounted E3 chain completes variance, correction, disposition and durable lock lifecycle',async()=>{
  const h=acceptanceHarness(),d=day('2026-08-25');
  assert.equal((await h.api.open(d)).day.state,'OPEN');
  assert.equal((await h.api.declare(d,{}, {invoiceId:'INV-MATCH',croId:'CRO-1',netAmount:100})).ok,true);
  assert.equal((await h.api.declare(d,{}, {invoiceId:'INV-PHANTOM',croId:'CRO-9',netAmount:50})).ok,true);

  assert.equal((await h.api.close(d)).code,'E3_FRESH_GRANT_REQUIRED','Staff cannot close');
  h.setRole('Store Manager');
  assert.equal((await h.api.close(d)).day.state,'CLOSED');

  h.failVerifiedRead(true);
  assert.equal((await h.api.importVerified(d)).code,'E3_VERIFIED_READ_FAILED');
  assert.equal((await h.api.load(d)).day.state,'CLOSED','failed import cannot advance state');
  h.failVerifiedRead(false);
  const imported=await h.api.importVerified(d);
  assert.equal(imported.day.state,'IMPORTED','one public import action completes the explicit imported transition');
  assert.equal(imported.day.sourceFacts,undefined,'verified facts are not exposed by the public gateway');
  assert.equal((await h.api.importVerified(d)).code,'IMPORT_INVALID','a repeated import cannot reapply facts or advance state');
  assert.equal((await h.api.load(d)).day.state,'IMPORTED');

  h.setRole('Staff');
  assert.equal((await h.api.reconcile(d)).code,'E3_FRESH_GRANT_REQUIRED','Staff cannot reconcile');
  h.setRole('Store Manager');
  h.setTime('2026-08-25T08:10:01.000Z');
  h.setReauth('2026-08-25T08:00:00.000Z');
  assert.equal((await h.api.reconcile(d)).code,'E3_FRESH_GRANT_REQUIRED','stale reauthentication cannot reconcile');
  assert.equal((await h.api.load(d)).day.outcomes.length,0,'denied reconciliation cannot mutate state');
  h.setReauth(null);
  const reconciled=await h.api.reconcile(d);
  assert.deepEqual(reconciled.day.outcomes.map(x=>x.outcome).sort(),['Matched','Phantom','Unclaimed']);

  h.setTime('2026-08-25T09:00:00.000Z');
  assert.equal((await h.api.correct(d,{reason:'assigned from till evidence'},{invoiceId:'INV-UNCLAIMED',croId:'CRO-2'})).ok,true,'manager may correct inside 24 hours');
  h.setTime('2026-08-26T09:00:01.000Z');
  assert.equal((await h.api.correct(d,{reason:'late manager attempt'},{invoiceId:'INV-UNCLAIMED',croId:'CRO-3'})).code,'OWNER_REQUIRED_AFTER_FREEZE');
  h.setRole('Owner');
  assert.equal((await h.api.correct(d,{reason:'owner late correction'},{invoiceId:'INV-UNCLAIMED',croId:'CRO-3'})).ok,true,'Owner may correct after 24 hours');

  assert.equal((await h.api.markReconciled(d,{},'VARIANCE')).day.state,'VARIANCE');
  assert.equal((await h.api.dispose(d,{reason:'evidence reviewed'},{invoiceId:'INV-PHANTOM',code:'SOURCE_EXCEPTION'})).ok,true);
  assert.equal((await h.api.lock(d)).day.state,'LOCKED');
  assert.equal((await h.api.correct(d,{reason:'locked edit'},{invoiceId:'INV-UNCLAIMED',croId:'CRO-4'})).code,'DAY_LOCKED');

  const restarted=h.restart();
  const durable=await restarted.load(d);
  assert.equal(durable.day.state,'LOCKED');
  assert.equal(durable.cycle,1);
  assert.equal(durable.day.dispositions.length,1);
});

test('real mounted E3 chain also completes the clean reconciled branch without a disposition',async()=>{
  const h=acceptanceHarness(),d=day('2026-08-26');
  h.setRole('Staff');
  await h.api.open(d);
  await h.api.declare(d,{}, {invoiceId:'INV-CLEAN',croId:'CRO-2',netAmount:300});
  h.setRole('Store Manager');
  await h.api.close(d);
  assert.equal((await h.api.importVerified(d)).day.state,'IMPORTED');
  const result=await h.api.reconcile(d);
  assert.equal(result.day.outcomes.every(x=>x.outcome==='Matched'),true);
  assert.equal((await h.api.markReconciled(d,{},'RECONCILED')).day.state,'RECONCILED');
  const locked=await h.api.lock(d);
  assert.equal(locked.day.state,'LOCKED');
  assert.equal(locked.day.dispositions.length,0);
});

test('public verified import reuses one composer-issued reauthentication context for both durable steps',async()=>{
  const h=acceptanceHarness(),d=day('2026-08-26');
  h.setRole('Staff');
  await h.api.open(d);
  await h.api.declare(d,{}, {invoiceId:'INV-CLEAN',croId:'CRO-2',netAmount:300});
  h.setRole('Store Manager');
  await h.api.close(d);
  h.useOneReauth();
  const imported=await h.api.importVerified(d);
  assert.equal(imported.day.state,'IMPORTED');
  assert.equal(h.reauthCalls(),1);
});

test('RECONCILE grant is privileged, role-bound and fresh before any state mutation',async()=>{
  const active=foundation.registerAuthority(authority);
  const request={domain:'E3',action:'RECONCILE',actorId:'manager-1',actorRole:'Store Manager',storeCode:'WLMHW',at:'2026-08-25T08:10:01.000Z',reauthenticatedAt:'2026-08-25T08:00:00.000Z'};
  assert.equal(foundation.authorize(active,{...request,actorRole:'Staff'}).code,'ETP_OPERATION_DENIED');
  assert.equal(foundation.authorize(active,request).code,'ETP_FRESH_REAUTH_REQUIRED');
  assert.equal(foundation.authorize(active,{...request,at:'2026-08-25T08:05:00.000Z'}).ok,true);
});
