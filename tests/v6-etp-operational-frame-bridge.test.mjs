import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source=fs.readFileSync(new URL('../www/etp-operational-frame-bridge.js',import.meta.url),'utf8');

test('binds the real parent privately and forwards only roots and scope provider',async()=>{let received;const parent={SaagarEtpOperationalShellComposer:{compose:value=>{received=value;return Promise.resolve({ok:true});}}},window={parent};vm.runInNewContext(source,{window,Object,Promise});const roots={e3:{},e4:{}},getScope=()=>({});assert.equal((await window.SaagarEtpOperationalFrameBridge.compose({roots,getScope})).ok,true);assert.equal(received.root,parent);assert.equal(received.roots,roots);assert.equal(received.getScope,getScope);assert.deepEqual(Object.keys(window.SaagarEtpOperationalFrameBridge).sort(),['compose','version']);});

test('fails closed when parent composer is absent and exports no parent handle',async()=>{const window={};window.parent={};vm.runInNewContext(source,{window,Object,Promise});const out=await window.SaagarEtpOperationalFrameBridge.compose({});assert.equal(out.code,'ETP_FRAME_COMPOSER_UNAVAILABLE');assert.equal('parent' in window.SaagarEtpOperationalFrameBridge,false);assert.equal(Object.isFrozen(window.SaagarEtpOperationalFrameBridge),true);});
