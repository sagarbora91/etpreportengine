import test from 'node:test';
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import fs from 'node:fs';
const require=createRequire(import.meta.url),presentation=require('../www/etp-e3-presentation.js'),i18n=require('../www/etp-operational-i18n.js');

class El{constructor(tag,doc){this.tagName=tag.toUpperCase();this.ownerDocument=doc;this.children=[];this.attributes={};this.listeners={};this.className='';this.textContent='';this.disabled=false;this.value='';this.firstChild=null;}appendChild(x){this.children.push(x);this.firstChild=this.children[0]||null;x.parentNode=this;return x;}removeChild(x){this.children.splice(this.children.indexOf(x),1);this.firstChild=this.children[0]||null;}setAttribute(k,v){this.attributes[k]=String(v);}getAttribute(k){return Object.prototype.hasOwnProperty.call(this.attributes,k)?this.attributes[k]:null;}addEventListener(k,v){this.listeners[k]=v;}removeEventListener(k){delete this.listeners[k];}querySelector(){return null;}}
class Doc{createElement(tag){return new El(tag,this);}}
const all=root=>[root,...root.children.flatMap(all)],body=root=>all(root).map(x=>x.textContent).filter(Boolean).join(' | '),host=()=>new El('div',new Doc());
function day(scope){return{storeId:scope.storeCode,businessDate:scope.businessDate,state:'OPEN',declarations:[],outcomes:[],unassignedQueue:[],dispositions:[],audit:[]};}

test('synthetic staff UAT switches role, store scope and language without changing governed values',async()=>{
  const matrix=[
    {role:'Staff',actorRole:'Staff',language:'en',action:'Add declaration'},
    {role:'Store Manager',actorRole:'Store Manager',language:'mr',action:'घोषणा जोडा'},
    {role:'Owner',actorRole:'Owner',language:'hi',action:'घोषणा जोड़ें'}
  ];
  for(const row of matrix){
    let scope={storeCode:'WLMHW',businessDate:'2026-08-25'},loads=0;const root=host();
    const mounted=presentation.create({host:root,facade:{load:async input=>{loads++;assert.deepEqual(input,scope);return{ok:true,day:day(scope),cycle:1};}},getScope:()=>scope,getActor:()=>({actorId:row.role.replace(/\s/g,'-').toUpperCase()+'-SYNTHETIC',actorRole:row.actorRole})});
    assert.equal((await mounted.controller.refresh()).ok,true);i18n.apply(root,row.language);
    assert.match(body(root),new RegExp(row.action));assert.match(body(root),/WLMHW/);assert.match(body(root),/2026-08-25/);
    const add=all(root).find(x=>x.getAttribute('data-e3-action')==='declare');assert.equal(add.disabled,false,'all approved roles may declare invoices');
    const close=all(root).find(x=>x.getAttribute('data-e3-action')==='close');assert.equal(close.disabled,row.role==='Staff','Staff cannot close a day');
    scope={storeCode:'HEMW',businessDate:'2026-08-26'};assert.equal((await mounted.controller.refresh()).ok,true);i18n.apply(root,row.language);
    assert.match(body(root),/HEMW/);assert.match(body(root),/2026-08-26/);assert.doesNotMatch(body(root),/WLMHW/);assert.equal(loads,2);
  }
});

test('synthetic language expansion gate covers every E3-E7 mobile stylesheet without weakening desktop layouts',()=>{
  for(const phase of ['e3','e4','e5','e6','e7']){
    const css=fs.readFileSync(new URL(`../www/etp-${phase}-presentation.css`,import.meta.url),'utf8');
    assert.match(css,/max-width:100%/,`${phase} containment`);assert.match(css,/min-height:44px/,`${phase} touch target`);
    assert.doesNotMatch(css,/100vw|overflow-x:\s*auto/,`${phase} must not require horizontal sliding`);
    assert.match(css,/@media\(min-width:|repeat\(auto-fit,/,`${phase} desktop geometry remains explicit or intrinsically responsive`);
  }
});

test('automation evidence is synthetic and cannot be represented as physical or approved staff acceptance',()=>{
  assert.equal(i18n.CATALOG_STATUS,'TEST_ONLY_UNAPPROVED');
  const evidence=fs.readFileSync(new URL('../docs/audit/V6-ETP-MULTILINGUAL-MOBILE-SYNTHETIC-UAT-2026-08-25.md',import.meta.url),'utf8');
  assert.match(evidence,/synthetic/i);assert.match(evidence,/not physical-device acceptance, translation approval or end-to-end staff sign-off/i);
});
