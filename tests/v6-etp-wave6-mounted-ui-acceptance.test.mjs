import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import mountApi from '../www/etp-operational-mount.js';
import e3Presentation from '../www/etp-e3-presentation.js';

class Element {
  constructor(tag, document) {
    this.tagName=String(tag).toUpperCase();this.ownerDocument=document;this.children=[];this.attributes={};this.listeners={};
    this.parentNode=null;this.className='';this.textContent='';this.disabled=false;this.value='';this.name='';this.type='';
  }
  get firstChild(){return this.children[0]||null;}
  get form(){let node=this.parentNode;while(node&&node.tagName!=='FORM')node=node.parentNode;return node||null;}
  appendChild(value){this.children.push(value);value.parentNode=this;return value;}
  removeChild(value){this.children.splice(this.children.indexOf(value),1);value.parentNode=null;}
  setAttribute(name,value){this.attributes[name]=String(value);}
  getAttribute(name){return Object.prototype.hasOwnProperty.call(this.attributes,name)?this.attributes[name]:null;}
  addEventListener(name,listener){this.listeners[name]=listener;}
  removeEventListener(name){delete this.listeners[name];}
  querySelector(selector){const match=/^\[name="([^"]+)"\]$/.exec(selector);return match?all(this).find(node=>node.name===match[1])||null:null;}
}
class Document { createElement(tag){return new Element(tag,this);} }
function all(node){return [node,...node.children.flatMap(all)];}
function visibleText(node){return all(node).map(item=>item.textContent).filter(Boolean).join(' | ');}
function action(root,name){return all(root).find(node=>node.getAttribute('data-e3-action')===name);}
function setForm(root,name,values){const form=all(root).find(node=>node.getAttribute('data-e3-form')===name);assert.ok(form,`missing ${name} form`);for(const [field,value] of Object.entries(values)){const input=form.querySelector(`[name="${field}"]`);assert.ok(input,`missing ${name}.${field}`);input.value=String(value);}return form;}
async function click(root,name){const target=action(root,name);assert.ok(target,`missing ${name} action in ${visibleText(root)}`);assert.equal(target.disabled,false,`${name} unexpectedly disabled`);root.listeners.click({target,preventDefault(){}});await waitUntil(()=>!root.attributes['data-e3-state']||root.attributes['data-e3-state']!=='busy');}
async function waitUntil(predicate){for(let attempt=0;attempt<100;attempt++){if(predicate())return;await new Promise(resolve=>setTimeout(resolve,0));}assert.fail('presentation did not settle');}

const scopeKey='WLMHW|2026-27|2026-08-01..2026-08-31';
const scope={scopeKey,storeCode:'WLMHW',businessDate:'2026-08-25'};
const actor={actorId:'owner-live',role:'Owner'};
const binding={scopeKey,generationId:'etp_'+'a'.repeat(32),receiptId:'receipt-a'};
function publicDay(state,cycle,audit=[],computed=false){return {storeId:'WLMHW',businessDate:'2026-08-25',state,declarations:[{invoiceId:'INV-1',croId:'CRO-1',netAmount:100}],outcomes:computed?[{invoiceId:'INV-1',outcome:'Misattributed',sourceCroId:'CRO-2',declaredCroId:'CRO-1'}]:[],unassignedQueue:[],dispositions:state==='VARIANCE'?[{invoiceId:'INV-1',code:'SOURCE_CONFIRMED'}]:[],audit:audit.map((event,index)=>({event,role:'Owner',at:`2026-08-25T10:0${index}:00.000Z`,reason:'accepted'})),cycle};}

function operationalFacade(){
  let state='OPEN',cycle=1,audit=[],computed=false;const calls=[];
  function result(event,next=state){calls.push(event);audit.push(event);state=next;return Promise.resolve({ok:true,day:publicDay(state,cycle,audit,computed),cycle});}
  return {calls,facade:{
    load:async received=>{calls.push('load');assert.equal(received.scopeKey,scopeKey);return {ok:true,day:publicDay(state,cycle,audit,computed),cycle};},
    open:()=>result('open','OPEN'),declare:(received,receivedActor,declaration)=>{assert.equal(received.scopeKey,scopeKey);assert.equal(receivedActor.actorId,'owner-live');assert.deepEqual(declaration,{invoiceId:'INV-1',croId:'CRO-1',netAmount:100});return result('declare');},
    close:()=>result('close','CLOSED'),importVerified:(received,_actor,receivedBinding)=>{assert.equal(received.scopeKey,scopeKey);assert.deepEqual(receivedBinding,binding);return result('import','IMPORTED');},
    reconcile:()=>{computed=true;return result('reconcile','IMPORTED');},markReconciled:(_scope,_actor,to)=>{assert.equal(to,'VARIANCE');return result('mark-variance','VARIANCE');},
    correct:(_scope,receivedActor,correction)=>{assert.equal(receivedActor.reason,'Wrong CRO attribution');assert.deepEqual(correction,{invoiceId:'INV-1',croId:'CRO-2'});return result('correct');},
    dispose:(_scope,receivedActor,disposition)=>{assert.equal(receivedActor.reason,'Source checked');assert.deepEqual(disposition,{invoiceId:'INV-1',code:'SOURCE_CONFIRMED'});return result('dispose');},
    lock:()=>result('lock','LOCKED'),startRestatementCycle:(received,_actor,receivedBinding)=>{assert.equal(received.scopeKey,scopeKey);assert.deepEqual(receivedBinding,binding);cycle++;computed=false;return result('restatement','OPEN');}
  }};
}

test('mounted form and button workflow reaches immutable lock and a new restatement cycle',async()=>{
  const document=new Document(),roots={e3:new Element('div',document),e4:new Element('div',document)},operation=operationalFacade();
  const mounted=mountApi.mount({roots,bridge:{e3:operation.facade,e4:{load(){}},getVerifiedBinding:()=>binding},presentations:{e3:e3Presentation,e4:{mount:()=>({ok:true,refresh:async()=>({ok:true})})}},getScope:()=>scope,getActor:()=>actor});
  assert.equal(mounted.ok,true);assert.equal((await mounted.controller.refresh()).ok,true);
  setForm(roots.e3,'declare',{invoiceId:'INV-1',croId:'CRO-1',netAmount:100});await click(roots.e3,'declare');
  await click(roots.e3,'close');await click(roots.e3,'import');await click(roots.e3,'reconcile');await click(roots.e3,'mark-variance');
  setForm(roots.e3,'correct',{invoiceId:'INV-1',croId:'CRO-2',reason:'Wrong CRO attribution'});await click(roots.e3,'correct');
  setForm(roots.e3,'dispose',{invoiceId:'INV-1',code:'SOURCE_CONFIRMED',reason:'Source checked'});await click(roots.e3,'dispose');
  await click(roots.e3,'lock');assert.match(visibleText(roots.e3),/Locked and immutable/);await click(roots.e3,'restatement');
  assert.match(visibleText(roots.e3),/cycle 2/);
  assert.deepEqual(operation.calls,['load','declare','close','import','reconcile','mark-variance','correct','dispose','lock','restatement']);
  assert.equal(mounted.controller.destroy().ok,true);assert.equal(roots.e3.children.length,0);
});

test('mounted scope identity is preserved for the governed backend and malformed identity fails closed',async()=>{
  const document=new Document(),root=new Element('div',document),seen=[];
  const made=e3Presentation.create({host:root,facade:{load:async received=>(seen.push(received),{ok:false,code:'EXPECTED'})},getScope:()=>scope,getActor:()=>({actorId:'owner-live',actorRole:'Owner'})});
  await made.controller.refresh();assert.equal(seen[0].scopeKey,scopeKey);
  const invalid=e3Presentation.create({host:new Element('div',document),facade:{load:async()=>assert.fail('invalid scope reached facade')},getScope:()=>({...scope,scopeKey:scopeKey.replace('WLMHW','HEMW')}),getActor:()=>({actorId:'owner-live',actorRole:'Owner'})});
  assert.equal(await invalid.controller.refresh(),undefined);
});

test('all mounted UI sources remain text-only and Android API23 compatible',()=>{
  for(const file of ['etp-e3-presentation.js','etp-operational-mount.js','etp-operational-module-host.js','etp-operational-shell-composer.js','etp-operational-frame-bridge.js']){
    const source=fs.readFileSync(new URL(`../www/${file}`,import.meta.url),'utf8');
    assert.doesNotMatch(source,/innerHTML|insertAdjacentHTML|outerHTML|indexedDB|Capacitor|SaagarEtpNativeStore|postMessage|fetch\s*\(|XMLHttpRequest|\?\.|\?\?/,file);
  }
});
