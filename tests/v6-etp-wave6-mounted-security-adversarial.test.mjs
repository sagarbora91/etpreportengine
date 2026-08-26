import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import bootstrap from '../www/etp-operational-bootstrap.js';
import foundation from '../www/etp-operational-foundation.js';
import mountApi from '../www/etp-operational-mount.js';
import composer from '../www/etp-operational-shell-composer.js';

const scopeKey='WLMHW|2026-27|2026-08-01..2026-08-31';
const scope={storeCode:'WLMHW',financialYear:'2026-27',periodStart:'2026-08-01',periodEnd:'2026-08-31',scopeKey,businessDate:'2026-08-25'};
const generation='etp_'+'b'.repeat(32),binding={scopeKey,generationId:generation,receiptId:'receipt-b'};
const authority={domain:'E3',status:'ACTIVE',sourceSha256:'a'.repeat(64),approvalId:'E3-OWNER-ACCEPTANCE',approvedAt:'2026-08-25T08:00:00Z',approvedByRole:'Owner',stores:['WLMHW']};
const blockedE4={ok:true,status:'BLOCKED',authorities:['TITAN_TARGET','FESTIVE_CALENDAR','CRO_IDENTITY_MAP','E4_POLICY_AUTHORITY'].map(sourceType=>({sourceType,status:'MISSING',sourceSha256:null})),sourceIntake:{status:'INCOMPLETE'},versions:[],activeVersion:null,pace:null,leave:{approvedCount:0},coverageShortfall:{amountPaise:0},actions:{publish:false,revise:false,reallocate:false}};

function bootstrapHarness(change={}){
  const calls=[];let currentBinding=binding;
  const options={foundation,store:{create(){}},runtime:{create:()=>({ok:true,runtime:{repository:{}}})},adapters:{createE3(){},createE4(){}},e3Engine:{},e4Engine:{},e3Orchestrator:{create(){}},e4Orchestrator:{create(){}},gateway:{create:()=>({ok:true,gateway:{E3:new Proxy({}, {get:(_target,name)=>request=>{calls.push({name,request});if(name==='close'&&Date.parse(request.at)-Date.parse(request.reauthenticatedAt)>300000)return {ok:false,code:'E3_FRESH_GRANT_REQUIRED',privateHandle:{secret:true}};return {ok:true};}}),E4:{}}})},verifiedJoin:{readE3(){},getBinding:key=>key===scopeKey?currentBinding:null,getNextBinding:()=>null},storage:{private:true},ownerSession:()=>({actorId:'owner-session',role:'Owner',storeCode:'WLMHW'}),reauth:()=> '2026-08-25T10:00:00Z',clock:()=> '2026-08-25T10:00:00Z',authorities:{E3:authority},...change};
  return {options,calls,setBinding:value=>{currentBinding=value;}};
}

test('caller spoofing and HEMW or cross-generation scope drift are denied at the live public facade',async()=>{
  const harness=bootstrapHarness(),operational=bootstrap.create(harness.options).operational;
  await operational.E3.declare(scope,{actorId:'attacker',actorRole:'Owner',reauthenticatedAt:'2099-01-01T00:00:00Z'},{invoiceId:'INV-1'});
  assert.equal(harness.calls[0].request.actorId,'owner-session');assert.equal(harness.calls[0].request.reauthenticatedAt,null);
  const hemw=await operational.E3.close({...scope,scopeKey:scopeKey.replace('WLMHW','HEMW'),storeCode:'HEMW'});assert.equal(hemw.code,'ETP_OPERATIONAL_SCOPE_DENIED');
  harness.setBinding({...binding,generationId:'etp_'+'c'.repeat(32)});await operational.E3.importVerified(scope);
  assert.equal(harness.calls.at(-1).request.binding.generationId,'etp_'+'c'.repeat(32));
  harness.setBinding(null);assert.equal((await operational.E3.importVerified(scope)).code,'ETP_OPERATIONAL_CONTEXT_UNAVAILABLE');
});

test('stale reauthentication is rejected and private downstream detail is removed',async()=>{
  const harness=bootstrapHarness({reauth:()=> '2026-08-25T09:00:00Z'}),operational=bootstrap.create(harness.options).operational;
  const denied=await operational.E3.close(scope);
  assert.deepEqual(denied,{ok:false,code:'E3_FRESH_GRANT_REQUIRED'});assert.doesNotMatch(JSON.stringify(denied),/privateHandle|secret/);
});

test('shell composer requires current WLMHW PASS receipt, Owner session and one approved reauthentication',async()=>{
  let mounted,reauthResult=false,receipt={scopeKey,storeCode:'WLMHW',activeGenerationId:generation,reconciliationStatus:'PASS'};
  const root={SaagarEtpModuleGateway:{readFacade:{inspectScope:async()=>({ok:true,currentReceipt:receipt})}},SaagarEtpOperationalBootstrap:{create:()=>({ok:true,operational:{E3:{load:async()=>({ok:true}),open:async()=>({ok:true}),declare:async()=>({ok:true}),close:async()=>({ok:true})},E4:{load:async()=>blockedE4},status:{E3:'READY_WLMHW',E4:'UNAVAILABLE_AUTHORITY_REQUIRED'}}})},SaagarEtpOperationalMount:{mount:value=>(mounted=value,{ok:true,controller:{refresh:async()=>({ok:true}),destroy(){}}})},SaagarEtpE3VerifiedJoin:{create:()=>({ok:true,reader:{loadDay:async()=>({ok:true})}})},SaagarEtpOperationalFoundation:{},SaagarEtpOperationalStore:{},SaagarEtpOperationalRuntime:{},SaagarEtpOperationalAdapters:{},SaagarEtpCroReconciliation:{},SaagarEtpTargetPlanning:{},SaagarEtpE3Orchestrator:{},SaagarEtpE4Orchestrator:{},SaagarEtpOperationalGateway:{},SaagarEtpE3Presentation:{},SaagarEtpE4Presentation:{},SaagarOwnerSession:{read:()=>({version:1,isOwner:true})},SaagarReauth:async()=>reauthResult,localStorage:{}};
  const made=await composer.compose({root,roots:{e3:{},e4:{}},getScope:()=>scope});assert.equal(made.ok,true);
  assert.equal((await mounted.bridge.e3.close(scope)).code,'ETP_OPERATIONAL_CONTEXT_UNAVAILABLE');
  reauthResult=true;assert.equal((await mounted.bridge.e3.close(scope)).ok,true);
  receipt={...receipt,reconciliationStatus:'FAIL'};assert.equal((await composer.compose({root,roots:{e3:{},e4:{}},getScope:()=>scope})).code,'ETP_SHELL_VERIFIED_SCOPE_UNAVAILABLE');
  assert.equal((await composer.compose({root,roots:{e3:{},e4:{}},getScope:()=>({...scope,storeCode:'HEMW',scopeKey:scopeKey.replace('WLMHW','HEMW')} )})).code,'ETP_SHELL_SCOPE_UNAVAILABLE');
});

test('concurrent refresh is serialized, queues one replay and never overlaps mounted presentations',async()=>{
  const document={createElement(){return new Element(document);}},roots={e3:new Element(document),e4:new Element(document)};let calls=0,inFlight=0,maxInFlight=0,releases=[];
  const mounted=mountApi.mount({roots,bridge:{e3:{},e4:{}},presentations:{e3:{create:()=>({ok:true,controller:{refresh:()=>{calls++;inFlight++;maxInFlight=Math.max(maxInFlight,inFlight);return new Promise(resolve=>releases.push(()=>{inFlight--;resolve({ok:true});}));},destroy(){}}})},e4:{mount:()=>({ok:true,refresh:async()=>({ok:true})})}},getScope:()=>scope,getActor:()=>({actorId:'owner-session',role:'Owner'})});
  const first=mounted.controller.refresh();await Promise.resolve();await Promise.resolve();const busy=await mounted.controller.refresh();assert.equal(busy.code,'ETP_OPERATIONAL_BUSY');
  releases.shift()();for(let i=0;i<20&&calls<2;i++)await new Promise(resolve=>setTimeout(resolve,0));assert.equal(calls,2);releases.shift()();assert.equal((await first).ok,true);assert.equal(maxInFlight,1);
});

class Element {constructor(document){this.ownerDocument=document;this.children=[];this.attributes={};this.textContent='';}get firstChild(){return this.children[0]||null;}appendChild(value){this.children.push(value);return value;}removeChild(value){this.children.splice(this.children.indexOf(value),1);}setAttribute(name,value){this.attributes[name]=String(value);}}

test('module remount destroys the prior controller and destroys stale concurrent composition',()=>{
  const source=fs.readFileSync(new URL('../www/modules/etp/index.html',import.meta.url),'utf8');
  assert.match(source,/sequence=\+\+operationalSequence/);assert.match(source,/operationalController\.destroy\(\)/);assert.match(source,/sequence!==operationalSequence/);assert.match(source,/made\.controller\.destroy\(\)/);
});
