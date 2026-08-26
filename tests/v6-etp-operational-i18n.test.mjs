import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import i18n from '../www/etp-operational-i18n.js';

class Node {
  constructor(tag='div',text=''){this.tagName=tag.toUpperCase();this.children=[];this.textContent=text;this.attributes={};}
  appendChild(child){this.children.push(child);return child;}
  setAttribute(name,value){this.attributes[name]=String(value);}
  getAttribute(name){return Object.prototype.hasOwnProperty.call(this.attributes,name)?this.attributes[name]:null;}
}
const leaf=(tag,text)=>new Node(tag,text);
const text=root=>[root,...root.children.flatMap(function all(node){return [node,...node.children.flatMap(all)];})].map(node=>node.textContent).filter(Boolean).join(' | ');

test('bounded operational catalog is explicitly test-only and falls back to English',()=>{
  assert.equal(i18n.CATALOG_STATUS,'TEST_ONLY_UNAPPROVED');
  assert.deepEqual([...i18n.LANGUAGES],['en','mr','hi']);
  assert.ok(i18n.phraseCount>=65);
  assert.equal(i18n.translate('E3 · CRO reconciliation','mr'),'E3 · CRO सामंजस्य');
  assert.equal(i18n.translate('E7 Service ETP verification ready','hi'),'E7 सेवा ETP सत्यापन तैयार');
  assert.equal(i18n.translate('Unknown business value','mr'),'Unknown business value');
  assert.equal(i18n.translate('Open day','unsupported'),'Open day');
});

test('language switches are reversible while scope, actor and business data remain untouched',()=>{
  const root=new Node();
  root.appendChild(leaf('h1','E6 exception monitoring ready'));
  root.appendChild(leaf('button','Add evidence'));
  root.appendChild(leaf('p','WLMHW|2026-27|2026-08-01..2026-08-31'));
  root.appendChild(leaf('p','CRO-001 · Owner · evidence-7'));
  assert.equal(i18n.apply(root,'hi').ok,true);
  assert.match(text(root),/E6 अपवाद निगरानी तैयार/);
  assert.match(text(root),/साक्ष्य जोड़ें/);
  assert.match(text(root),/WLMHW\|2026-27/);
  assert.match(text(root),/CRO-001 · Owner · evidence-7/);
  i18n.apply(root,'mr');
  assert.match(text(root),/E6 अपवाद निरीक्षण तयार/);
  assert.match(text(root),/पुरावा जोडा/);
  i18n.apply(root,'en');
  assert.match(text(root),/E6 exception monitoring ready/);
  assert.equal(root.attributes.lang,'en');
  assert.equal(root.attributes['data-etp-i18n-surface'],'');
});

test('adapter recovers English when the shared runtime localized a node first',()=>{
  const root=new Node();root.appendChild(leaf('button','पुरावा जोडा'));
  i18n.apply(root,'en');assert.equal(root.children[0].textContent,'Add evidence');
  i18n.apply(root,'hi');assert.equal(root.children[0].textContent,'साक्ष्य जोड़ें');
});

test('editable and business-table surfaces are excluded from localization',()=>{
  const root=new Node(),input=leaf('input','Add evidence'),tbody=leaf('tbody','Close with evidence'),optOut=leaf('p','Open day');
  optOut.setAttribute('data-no-i18n','');root.appendChild(input);root.appendChild(tbody);root.appendChild(optOut);
  i18n.apply(root,'hi');
  assert.equal(input.textContent,'Add evidence');
  assert.equal(tbody.textContent,'Close with evidence');
  assert.equal(optOut.textContent,'Open day');
});

test('all E3-E7 readiness and primary action landmarks have Marathi and Hindi test renderings',()=>{
  const phrases=[
    'E3 · CRO reconciliation','Load selected day','Apply correction',
    'E4 target planning ready','Publish target plan','Reallocate targets',
    'E6 exception monitoring ready','Acknowledge','Close with evidence',
    'E5 incentive control ready','Calculate incentive','Calculate clawback',
    'E7 Service ETP verification ready','Run controlled S003 ↔ S004 verification','Immutable verification and closure history'
  ];
  for(const phrase of phrases)for(const language of ['mr','hi'])assert.notEqual(i18n.translate(phrase,language),phrase,`${language}: ${phrase}`);
});

test('auto boot discovers only operational roots and responds to the shell ST_LANG event',()=>{
  const roots=[new Node(),new Node()];roots[0].appendChild(leaf('h1','E4 target planning ready'));roots[1].appendChild(leaf('h1','E7 Service ETP verification ready'));
  let listener=null,selector='';const doc={querySelectorAll(value){selector=value;return roots;}};
  const win={SaagarI18n:{getLanguage:()=> 'en'},addEventListener(type,fn){if(type==='message')listener=fn;},removeEventListener(){listener=null;}};
  const boot=i18n.autoBoot(doc,win);assert.equal(boot.ok,true);assert.equal(boot.count,2);assert.match(selector,/data-etp-operational-e3/);assert.match(selector,/data-etp-operational-e7/);
  listener({data:{type:'ST_LANG',lang:'mr'}});
  assert.match(text(roots[0]),/E4 लक्ष्य नियोजन तयार/);assert.match(text(roots[1]),/E7 सेवा ETP पडताळणी तयार/);
  boot.destroy();assert.equal(listener,null);
});

test('implementation uses text sinks and mobile containment without horizontal-scroll dependency',()=>{
  const source=fs.readFileSync(new URL('../www/etp-operational-i18n.js',import.meta.url),'utf8');
  const css=fs.readFileSync(new URL('../www/etp-operational-i18n.css',import.meta.url),'utf8');
  assert.doesNotMatch(source,/innerHTML|outerHTML|insertAdjacentHTML|eval\s*\(|new Function|fetch\s*\(|XMLHttpRequest/);
  assert.match(source,/textContent/);assert.match(source,/nodeValue/);assert.match(source,/data-no-i18n/);assert.match(source,/TBODY/);
  assert.match(css,/min-width:0/);assert.match(css,/max-width:100%/);assert.match(css,/min-height:44px/);assert.match(css,/@media\(max-width:480px\)/);assert.match(css,/@media\(min-width:900px\)/);
  assert.doesNotMatch(css,/100vw|overflow-x:\s*auto/);
});

test('production shell inventories the adapter and the ETP frame boots its script and stylesheet locally',()=>{
  const shell=fs.readFileSync(new URL('../www/index.html',import.meta.url),'utf8'),frame=fs.readFileSync(new URL('../www/modules/etp/index.html',import.meta.url),'utf8');
  assert.match(shell,/<script src="etp-operational-i18n\.js"><\/script>/);
  assert.match(frame,/<script src="\.\.\/\.\.\/etp-operational-i18n\.js"><\/script>/);
  assert.match(frame,/<link rel="stylesheet" href="\.\.\/\.\.\/etp-operational-i18n\.css">/);
  assert.doesNotMatch(frame,/https?:\/\/[^"']*etp-operational-i18n/);
});
