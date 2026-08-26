import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import foundation from '../www/etp-operational-foundation.js';
import bootstrap from '../www/etp-operational-bootstrap.js';

const scopeKey='WLMHW|2026-27|2026-08-01..2026-08-31',binding={scopeKey,generationId:'etp_'+'b'.repeat(32),receiptId:'receipt-1'};
const authority=(domain,hash)=>({domain,status:'ACTIVE',sourceSha256:hash.repeat(64),approvalId:domain+'-OWNER-001',approvedAt:'2026-08-25T08:00:00Z',approvedByRole:'Owner',stores:['WLMHW']});
function options(extra={}){
  const calls=[];
  const E3=new Proxy({}, {get:(_,name)=>r=>{calls.push({domain:'E3',name,r});return Promise.resolve({ok:true,day:null,cycle:0});}});
  const E4={
    readiness:()=>({ok:true,readiness:{sourceHashes:{TITAN_TARGET:'1'.repeat(64),FESTIVE_CALENDAR:'2'.repeat(64),CRO_IDENTITY_MAP:'3'.repeat(64),E4_POLICY_AUTHORITY:'4'.repeat(64)}}}),
    load:()=>({ok:true,model:{sourceHashes:{TITAN_TARGET:'1'.repeat(64),FESTIVE_CALENDAR:'2'.repeat(64),CRO_IDENTITY_MAP:'3'.repeat(64),E4_POLICY_AUTHORITY:'4'.repeat(64)},versions:[],activeVersion:null}}),
    intake:r=>({ok:true,r}),publish:r=>({ok:true,r}),revise:r=>({ok:true,r}),reallocate:r=>({ok:true,r}),pace:r=>({ok:true,pace:{plan:{coverageShortfall:{amountPaise:0}}},r})
  };
  const E6={load:()=>({ok:false,code:'E6_GATEWAY_AUTHORITY_UNAVAILABLE'}),refresh:r=>({ok:true,r}),acknowledge:r=>({ok:true,r}),reassign:r=>({ok:true,r}),addEvidence:r=>({ok:true,r}),close:r=>({ok:true,r})};
  const gatewayApi={create:()=>({ok:true,gateway:{E3,E4,E6}})};
  return {foundation,store:{create(){}},runtime:{create:()=>({ok:true,runtime:{repository:{put(){},get(){},list(){},canReadVerifiedScope(){return true;}}}})},adapters:{createE3(){},createE4(){},createE6(){}},e3Engine:{},e4Engine:{},e6Engine:{evaluate(){},transition(){},appendEvidence(){}},e3Orchestrator:{create(){}},e4Orchestrator:{create(){}},gateway:gatewayApi,verifiedJoin:{readE3(){},getBinding:()=>binding,getNextBinding:()=>({...binding,generationId:'etp_'+'c'.repeat(32)})},storage:{},ownerSession:()=>({actorId:'owner-1',role:'Owner',storeCode:'WLMHW'}),reauth:()=> '2026-08-25T09:59:00Z',clock:()=> '2026-08-25T10:00:00Z',authorities:{E3:authority('E3','a')},calls,...extra};
}

test('bootstraps idempotently with approved E3 WLMHW policy and keeps E4 unavailable',async()=>{const o=options(),first=bootstrap.create(o),second=bootstrap.create(o);assert.equal(first,second);assert.equal(first.ok,true);assert.equal(first.operational.status.E3,'READY_WLMHW');assert.equal(first.operational.status.E4,'UNAVAILABLE_AUTHORITY_REQUIRED');assert.equal((await first.operational.E4.load({scope:{scopeKey}})).status,'BLOCKED');assert.equal(Object.isFrozen(first.operational),true);});

test('rejects missing, HEMW or non-owner E3 authority records',()=>{const missing=options();missing.authorities={};assert.equal(bootstrap.create(missing).code,'ETP_BOOTSTRAP_E3_AUTHORITY_REQUIRED');const hemw=options();hemw.authorities.E3={...authority('E3','a'),stores:['HEMW']};assert.equal(bootstrap.create(hemw).code,'ETP_BOOTSTRAP_E3_AUTHORITY_REQUIRED');const manager=options();manager.authorities.E3={...authority('E3','a'),approvedByRole:'Store Manager'};assert.equal(bootstrap.create(manager).code,'ETP_BOOTSTRAP_E3_AUTHORITY_REQUIRED');});

test('translates actor and fresh reauthentication but denies HEMW and unavailable context',async()=>{const o=options(),api=bootstrap.create(o).operational;await api.E3.close({scopeKey,businessDate:'2026-08-25'});const sent=o.calls[0].r;assert.equal(sent.actorId,'owner-1');assert.equal(sent.actorRole,'Owner');assert.equal(sent.reauthenticatedAt,'2026-08-25T09:59:00.000Z');assert.equal(sent.storeCode,undefined);assert.equal((await api.E3.close({scopeKey:scopeKey.replace('WLMHW','HEMW'),businessDate:'2026-08-25'})).code,'ETP_OPERATIONAL_SCOPE_DENIED');const broken=options({reauth:()=>null});assert.equal((await bootstrap.create(broken).operational.E3.close({scopeKey,businessDate:'2026-08-25'})).code,'ETP_OPERATIONAL_CONTEXT_UNAVAILABLE');});

test('E4 becomes callable only with a separately injected approved authority',async()=>{const o=options();o.authorities.E4=authority('E4','e');const api=bootstrap.create(o).operational,loaded=await api.E4.load({scope:{scopeKey}});assert.equal(api.status.E4,'AUTHORITY_INJECTED');assert.equal(loaded.status,'READY');assert.equal(loaded.authorities.length,4);assert.equal((await api.E4.intake({scope:{scopeKey},input:{operationId:'i1'}})).ok,true);assert.equal((await api.E4.publish({scope:{scopeKey},input:{operationId:'p1'}})).ok,true);assert.equal((await api.E4.pace({scope:{scopeKey},input:{versionId:'v1'}})).ok,true);});

test('runtime corruption and internal errors are reduced to stable status codes',()=>{const corrupt=options({runtime:{create:()=>({ok:false,code:'ETP_RUNTIME_CORRUPT',privateDetail:'raw payload'})}}),out=bootstrap.create(corrupt);assert.deepEqual(out,{ok:false,code:'ETP_RUNTIME_CORRUPT'});const thrown=options({runtime:{create(){throw new Error('disk path');}}});assert.equal(bootstrap.create(thrown).code,'ETP_BOOTSTRAP_RUNTIME_UNAVAILABLE');});

test('public surface exposes only frozen E3, E4, E5, E6 and status without repository or raw/native capabilities',()=>{const api=bootstrap.create(options()).operational;assert.deepEqual(Object.keys(api).sort(),['E3','E4','E5','E6','status']);assert.equal(api.status.E5,'UNAVAILABLE_AUTHORITY_REQUIRED');assert.equal(api.status.E6,'UNAVAILABLE_AUTHORITY_REQUIRED');const serialized=JSON.stringify(api);assert.doesNotMatch(serialized,/repository|storage|native|raw|verifiedJoin/);const source=fs.readFileSync(new URL('../www/etp-operational-bootstrap.js',import.meta.url),'utf8');assert.doesNotMatch(source,/localStorage|sessionStorage|indexedDB|Capacitor|SaagarEtpNativeStore|window\.|document\.|readFacts|rawRows|sourceFacts|fetch\s*\(|XMLHttpRequest|\?\.|\?\?/);});
