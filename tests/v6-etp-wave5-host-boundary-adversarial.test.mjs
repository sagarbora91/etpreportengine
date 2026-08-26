import test from 'node:test';
import assert from 'node:assert/strict';
import bootstrap from '../www/etp-operational-bootstrap.js';
import mount from '../www/etp-operational-mount.js';
import foundation from '../www/etp-operational-foundation.js';

const scopeKey='WLMHW|2026-27|2026-08-01..2026-08-31';
const generation=id=>`etp_${id.repeat(32)}`;
const authority=domain=>({domain,status:'ACTIVE',sourceSha256:'a'.repeat(64),approvalId:`${domain}-OWNER-001`,approvedAt:'2026-08-25T08:00:00Z',approvedByRole:'Owner',stores:['WLMHW']});

function harness(extra={}) {
  let binding={scopeKey,generationId:generation('b'),receiptId:'receipt-live'};
  const calls=[];
  const E3=new Proxy({}, {get:(_target,name)=>request=>{
    calls.push({name,request});
    if (name==='close' && Date.parse(request.at)-Date.parse(request.reauthenticatedAt)>5*60*1000) {
      return Promise.resolve({ok:false,code:'E3_REAUTH_REQUIRED',debug:{storage:'private'}});
    }
    return Promise.resolve({ok:true});
  }});
  const options={
    foundation,
    store:{create(){}},
    runtime:{create:()=>({ok:true,runtime:{repository:{canReadVerifiedScope(){return true;}}}})},
    adapters:{createE3(){},createE4(){}},e3Engine:{},e4Engine:{},
    e3Orchestrator:{create(){}},e4Orchestrator:{create(){}},
    gateway:{create:()=>({ok:true,gateway:{E3,E4:{readiness:()=>({ok:false,code:'E4_BLOCKED'})}}})},
    verifiedJoin:{readE3(){},getBinding:key=>key===scopeKey?binding:null,getNextBinding:()=>null},
    storage:{privateHandle:true},
    ownerSession:()=>({actorId:'owner-live',role:'Owner',storeCode:'WLMHW'}),
    reauth:()=> '2026-08-25T09:59:00Z',clock:()=> '2026-08-25T10:00:00Z',
    authorities:{E3:authority('E3')},...extra
  };
  return {options,calls,setBinding:value=>{binding=value;}};
}

test('caller-supplied actor fields cannot spoof the session-owned principal',async()=>{
  const h=harness(),api=bootstrap.create(h.options).operational;
  await api.E3.declare({scopeKey,businessDate:'2026-08-25'},{actorId:'attacker',actorRole:'Owner',reauthenticatedAt:'2099-01-01T00:00:00Z'},{invoiceId:'INV-1'});
  assert.equal(h.calls.length,1);
  assert.equal(h.calls[0].request.actorId,'owner-live');
  assert.equal(h.calls[0].request.actorRole,'Owner');
  assert.equal(h.calls[0].request.reauthenticatedAt,null);
});

test('cross-store and malformed scope attempts fail before any operational call',async()=>{
  const h=harness(),api=bootstrap.create(h.options).operational;
  for (const attempted of [
    'HEMW|2026-27|2026-08-01..2026-08-31',
    'WLMHW|2026-27|2026-08-31..2026-08-01',
    'WLMHW|2026-27|2026-08-01..2026-08-31|HEMW'
  ]) assert.equal((await api.E3.close({scopeKey:attempted,businessDate:'2026-08-25'})).ok,false);
  assert.equal(h.calls.length,0);
});

test('each operation rebinds receipt and generation and fails closed when binding disappears',async()=>{
  const h=harness(),api=bootstrap.create(h.options).operational;
  await api.E3.importVerified({scopeKey,businessDate:'2026-08-25'});
  h.setBinding({scopeKey,generationId:generation('c'),receiptId:'receipt-restated'});
  await api.E3.importVerified({scopeKey,businessDate:'2026-08-25'});
  h.setBinding(null);
  const denied=await api.E3.importVerified({scopeKey,businessDate:'2026-08-25'});
  assert.deepEqual(h.calls.map(x=>x.request.binding.receiptId),['receipt-live','receipt-live','receipt-restated','receipt-restated']);
  assert.equal(denied.code,'ETP_OPERATIONAL_CONTEXT_UNAVAILABLE');
  assert.equal(h.calls.length,4);
});

test('stale reauthentication remains rejectable downstream and private rejection detail is not returned',async()=>{
  const h=harness({reauth:()=> '2026-08-25T09:00:00Z'}),api=bootstrap.create(h.options).operational;
  const result=await api.E3.close({scopeKey,businessDate:'2026-08-25'});
  assert.deepEqual(result,{ok:false,code:'E3_REAUTH_REQUIRED'});
  assert.doesNotMatch(JSON.stringify(result),/storage|private/);
});

test('missing live dependencies fail closed without returning injected handles',()=>{
  const required=['storage','ownerSession','reauth','clock','verifiedJoin','gateway'];
  for (const name of required) {
    const h=harness(); delete h.options[name];
    assert.deepEqual(bootstrap.create(h.options),{ok:false,code:'ETP_BOOTSTRAP_DEPENDENCY_UNAVAILABLE'},name);
  }
});

class El { constructor(doc){this.ownerDocument=doc;this.children=[];this.attributes={};this.textContent='';} get firstChild(){return this.children[0]||null;} appendChild(v){this.children.push(v);return v;} removeChild(v){this.children.splice(this.children.indexOf(v),1);} setAttribute(k,v){this.attributes[k]=String(v);} }
const doc={createElement:()=>new El(doc)};

test('mounted presentation receives narrow facades only and controller cannot escape privileged handles',()=>{
  const roots={e3:new El(doc),e4:new El(doc)},privileged={storage:{secret:true},nativeStore:{readFacts(){}}};
  let capturedE3,capturedE4;
  const bridge={e3:Object.freeze({load(){}}),e4:Object.freeze({load(){}})};
  const presentations={
    e3:{create:o=>{capturedE3=o;return {ok:true,controller:{refresh:async()=>({ok:true}),destroy(){}}};}},
    e4:{mount:(_host,facade,o)=>{capturedE4={facade,o};return {ok:true,refresh:async()=>({ok:true})};}}
  };
  const out=mount.mount({roots,bridge,presentations,getScope:()=>({storeCode:'WLMHW'}),getActor:()=>({actorId:'owner-live',role:'Owner'}),...privileged});
  assert.equal(out.ok,true);
  assert.equal(capturedE3.facade,bridge.e3);
  assert.equal(capturedE4.facade,bridge.e4);
  assert.deepEqual(Object.keys(out.controller).sort(),['VERSION','destroy','refresh']);
  assert.doesNotMatch(JSON.stringify(out),/secret|nativeStore|readFacts|storage/);
});
