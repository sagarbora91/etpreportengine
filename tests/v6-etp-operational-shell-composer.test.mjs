import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import composer from '../www/etp-operational-shell-composer.js';

const scope={storeCode:'WLMHW',financialYear:'2026-27',periodStart:'2026-08-01',periodEnd:'2026-08-31',scopeKey:'WLMHW|2026-27|2026-08-01..2026-08-31',businessDate:'2026-08-25'};
const generation='etp_'+'a'.repeat(32);
function setup(change={}){let captured={},destroyed=0,reauth=0;const E3={load(){return{ok:true};},lock(){return{ok:typeof captured.bootstrap.reauth('LOCK')==='string'};}};const operational={E3,E4:{load(){}},E6:{load(){}},E5:{load(){}},status:Object.freeze({E3:'READY_WLMHW',E4:'UNAVAILABLE_AUTHORITY_REQUIRED',E6:'UNAVAILABLE_AUTHORITY_REQUIRED',E5:'UNAVAILABLE_AUTHORITY_REQUIRED'})};const root={
  SaagarEtpModuleGateway:{readFacade:{inspectScope:async()=>({ok:true,currentReceipt:{scopeKey:scope.scopeKey,storeCode:'WLMHW',activeGenerationId:generation,reconciliationStatus:'PASS'}}),queryReport:async()=>({ok:false})}},
  SaagarEtpOperationalBootstrap:{create:o=>(captured.bootstrap=o,{ok:true,operational})},SaagarEtpOperationalMount:{mount:o=>(captured.mount=o,{ok:true,controller:{refresh:async()=>({ok:true}),destroy(){destroyed++;}}})},
  SaagarEtpE3VerifiedJoin:{create:o=>(captured.join=o,{ok:true,reader:{loadDay:async day=>({ok:true,envelope:{source:'ETP_VERIFIED',scopeKey:scope.scopeKey,generationId:generation,receiptId:'receipt-'+generation.slice(4),rows:[],day}})}})},
  SaagarEtpOperationalFoundation:{},SaagarEtpOperationalStore:{},SaagarEtpOperationalRuntime:{},SaagarEtpOperationalAdapters:{},SaagarEtpCroReconciliation:{},SaagarEtpTargetPlanning:{},SaagarEtpExceptionMonitor:{},SaagarEtpIncentiveControl:{},SaagarEtpE5PayrollBridge:{},SaagarEtpE3Orchestrator:{},SaagarEtpE4Orchestrator:{},SaagarEtpOperationalGateway:{},SaagarEtpE3Presentation:{},SaagarEtpE4Presentation:{},SaagarEtpE6Presentation:{},SaagarEtpE5Presentation:{},
  SaagarOwnerSession:{read:()=>({version:1,isOwner:true,role:'Owner'})},SaagarReauth:async()=>{reauth++;return true;},localStorage:{getItem(){},setItem(){},removeItem(){}},...change};return{root,captured,get destroyed(){return destroyed;},get reauth(){return reauth;}};}

test('composes current verified receipt, Gate-0 E3 authority, durable bootstrap and all mounted domains',async()=>{const x=setup(),out=await composer.compose({root:x.root,roots:{e3:{},e4:{},e6:{},e5:{}},getScope:()=>scope});assert.equal(out.ok,true);assert.equal(x.captured.join.binding.receiptId,'receipt-'+'a'.repeat(32));assert.equal(x.captured.bootstrap.authorities.E3.approvalId,'E3-OWNER-2026-08-25-V1');assert.equal(x.captured.bootstrap.authorities.E4,undefined);assert.equal(x.captured.bootstrap.e5Engine,x.root.SaagarEtpIncentiveControl);assert.equal(x.captured.bootstrap.e5PayrollBridge,x.root.SaagarEtpE5PayrollBridge);assert.equal(x.captured.bootstrap.storage,x.root.localStorage);assert.equal(typeof x.captured.mount.bridge.e5.load,'function');assert.equal(x.captured.mount.presentations.e5,x.root.SaagarEtpE5Presentation);assert.deepEqual(Object.keys(out).sort(),['controller','ok','status']);assert.equal(out.status.E5,'UNAVAILABLE_AUTHORITY_REQUIRED');});

test('fails closed for HEMW, non-owner sessions, unverified receipts and missing dependencies',async()=>{
  let x=setup(),hemw={...scope,storeCode:'HEMW',scopeKey:scope.scopeKey.replace('WLMHW','HEMW')};
  assert.equal((await composer.compose({root:x.root,roots:{e3:{},e4:{},e6:{},e5:{}},getScope:()=>hemw})).code,'ETP_SHELL_SCOPE_UNAVAILABLE');
  x=setup({SaagarOwnerSession:{read:()=>({version:1,isOwner:false,role:'Store Manager'})}});
  assert.equal((await composer.compose({root:x.root,roots:{e3:{},e4:{},e6:{},e5:{}},getScope:()=>scope})).code,'ETP_SHELL_OWNER_SESSION_REQUIRED');
  x=setup();x.root.SaagarEtpModuleGateway.readFacade.inspectScope=async()=>({ok:true,currentReceipt:{...scope,activeGenerationId:generation,reconciliationStatus:'FAIL'}});
  assert.equal((await composer.compose({root:x.root,roots:{e3:{},e4:{},e6:{},e5:{}},getScope:()=>scope})).code,'ETP_SHELL_VERIFIED_SCOPE_UNAVAILABLE');
  assert.equal((await composer.compose({root:{},roots:{e3:{},e4:{},e6:{},e5:{}},getScope:()=>scope})).code,'ETP_SHELL_DEPENDENCY_UNAVAILABLE');
});

test('trusted owner and one-use reauthentication providers are translated without exposing them',async()=>{const x=setup(),out=await composer.compose({root:x.root,roots:{e3:{},e4:{},e6:{},e5:{}},getScope:()=>scope}),session=x.captured.bootstrap.ownerSession(),locked=await x.captured.mount.bridge.e3.lock({});
  assert.equal(session.actorId,'OWNER_SESSION');assert.equal(session.role,'Owner');assert.equal(session.storeCode,'WLMHW');assert.equal(locked.ok,true);assert.equal(x.reauth,1);assert.equal(x.captured.bootstrap.reauth('LOCK'),null);assert.equal('storage' in out,false);assert.equal('readFacade' in out,false);
});

test('binding adapter rejects drift and initial refresh failure destroys the mount',async()=>{const x=setup(),out=await composer.compose({root:x.root,roots:{e3:{},e4:{},e6:{},e5:{}},getScope:()=>scope}),verified=x.captured.bootstrap.verifiedJoin;assert.equal((await verified.readE3({scopeKey:'wrong',generationId:generation,receiptId:'receipt-'+generation.slice(4),businessDate:'2026-08-25'})).code,'E3_VERIFIED_BINDING_INVALID');assert.equal(verified.getNextBinding(),null);const bad=setup({SaagarEtpOperationalMount:{mount:()=>({ok:true,controller:{refresh:async()=>({ok:false}),destroy(){bad.wasDestroyed=true;}}})}});assert.equal((await composer.compose({root:bad.root,roots:{e3:{},e4:{},e6:{},e5:{}},getScope:()=>scope})).code,'ETP_SHELL_INITIAL_REFRESH_FAILED');assert.equal(bad.wasDestroyed,true);});

test('source is API23-safe and never exposes raw, native or cross-window handles',()=>{const source=fs.readFileSync(new URL('../www/etp-operational-shell-composer.js',import.meta.url),'utf8');assert.doesNotMatch(source,/innerHTML|insertAdjacentHTML|indexedDB|Capacitor|SaagarEtpNativeStore|window\.parent|parent\.|postMessage|readFacts|rawRows|sourceFacts|fetch\s*\(|XMLHttpRequest|\?\.|\?\?/);assert.match(source,/SaagarOwnerSession/);assert.match(source,/SaagarReauth/);assert.equal(Object.isFrozen(composer.GATE0_E3_AUTHORITY),true);});

test('Owner mode is authoritative even when a staff-role selector remains visible',async()=>{const x=setup({SaagarOwnerSession:{read:()=>({version:1,isOwner:true,role:'Store Manager'})}}),out=await composer.compose({root:x.root,roots:{e3:{},e4:{},e6:{},e5:{}},getScope:()=>scope});assert.equal(out.ok,true);assert.equal(x.captured.bootstrap.ownerSession().actorRole,'Owner');});
