import test from 'node:test';
import assert from 'node:assert/strict';
import hostApi from '../www/etp-e7-module-host.js';

class Node{constructor(doc){this.ownerDocument=doc;this.children=[];this.attributes={};this.className='';this.textContent='';}get firstChild(){return this.children[0]||null;}appendChild(x){this.children.push(x);return x;}removeChild(x){this.children.splice(this.children.indexOf(x),1);}setAttribute(k,v){this.attributes[k]=String(v);}}
const make=()=>{const doc={createElement:()=>new Node(doc)},root=new Node(doc);doc.querySelector=s=>s==='[data-etp-operational-e7]'?root:null;return{doc,root};};
const content=node=>[node.textContent,...node.children.flatMap(content)].filter(Boolean).join(' | ');

test('mounts E7 through an isolated host without changing retail operational roots',async()=>{const {doc}=make();let refresh=0;const made=hostApi.mount({document:doc,facade:{load(){}},presentation:{mount(host,facade,options){assert.ok(host);assert.equal(typeof facade.load,'function');assert.equal(typeof options.getScope,'function');return{ok:true,refresh:async()=>{refresh++;return{ok:true};}};}},getScope:()=>({scopeKey:'SERVICE_ETP_V1|SC01|2026-27|2026-08-01..2026-08-31'}),getActor:()=>null});assert.equal(made.status,'MOUNTED');assert.equal((await made.controller.refresh()).ok,true);assert.equal(refresh,1);});

test('missing service facade renders an honest deferred state and never guesses authority',async()=>{const {doc,root}=make(),made=hostApi.mount({document:doc,presentation:{mount(){}},getScope:()=>null,getActor:()=>null});assert.equal(made.ok,true);assert.equal(made.status,'BLOCKED');assert.match(content(root),/BLOCKED \/ deferred/);assert.match(content(root),/E7_AUTHORITY_DEFERRED/);assert.equal((await made.controller.refresh()).code,'E7_AUTHORITY_DEFERRED');});

test('dependency failure is fail-closed',()=>{const {doc,root}=make(),out=hostApi.mount({document:doc});assert.equal(out.code,'E7_MODULE_HOST_DEPENDENCY_UNAVAILABLE');assert.match(content(root),/E7_PRESENTATION_DEPENDENCY_UNAVAILABLE/);});
