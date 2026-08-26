import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {createRequire} from 'node:module';

const require=createRequire(import.meta.url);
const demo=require('../www/etp-demo-operational.js');
const e4=require('../www/etp-e4-presentation.js');
const e5=require('../www/etp-e5-presentation.js');
const e6=require('../www/etp-e6-presentation.js');
const e7=require('../www/etp-e7-presentation.js');
const verified=require('../www/etp-verified-presentation.js');

test('demo authority models satisfy the real E4-E7 presentation contracts',async()=>{
  assert.equal(demo.syntheticOnly,true);
  assert.ok(e4.cleanModel(demo.models.e4()));
  assert.ok(e5.cleanModel(demo.models.e5()));
  assert.ok(e6.cleanModel(demo.models.e6()));
  assert.ok(e7.cleanModel(demo.models.e7()));
  await demo.e7Facade.verify({});
  const verified=demo.models.e7();
  assert.equal(verified.status,'READY');
  assert.ok(e7.cleanModel(verified));
  assert.equal(verified.state.runs[0].result.summary.jobCount,3);
  assert.equal(verified.state.runs[0].result.summary.discrepancyCount,1);
});

test('demo scope and E7 service source are explicit, synthetic and separate',async()=>{
  const listed=await demo.readGateway.listScopes();
  assert.equal(listed.ok,true);
  assert.equal(listed.scopes.length,1);
  assert.match(listed.scopes[0].scope.scopeKey,/^WLMHW\|2026-27\|/);
  assert.match(demo.serviceScope.scopeKey,/^SERVICE_ETP_V1\|DEMO-SC01\|/);
  assert.notEqual(demo.scope.scopeKey,demo.serviceScope.scopeKey);
  assert.match(demo.models.e5().authority.approvalId,/DEMO.*NOT-REAL/);
  assert.match(demo.models.e6().authority.approvalId,/DEMO.*NOT-REAL/);
  const summary=await verified.load(demo.readGateway,demo.scope);
  assert.equal(summary.ok,true);
  assert.equal(summary.rowCount,5377);
});

test('demo retail composer accepts the public module scope without an internal scopeKey',async()=>{
  let mounted;
  const root={
    SaagarEtpOperationalMount:{mount(options){mounted=options;return{ok:true,controller:{refresh:async()=>({ok:true})}};}},
    SaagarEtpE3Presentation:{},SaagarEtpE4Presentation:{},SaagarEtpE5Presentation:{},SaagarEtpE6Presentation:{}
  };
  const publicScope={storeCode:'WLMHW',financialYear:'2026-27',periodStart:'2026-08-01',periodEnd:'2026-08-31'};
  const result=await demo.composeRetail({root,roots:{e3:{},e4:{},e5:{},e6:{}},getScope:()=>publicScope});
  assert.equal(result.ok,true);
  assert.equal(result.status.syntheticOnly,true);
  assert.equal(mounted.getScope().scopeKey,demo.scope.scopeKey);
});

test('clean source loads the demo facades only behind the strict seeded-build guard',()=>{
  const index=fs.readFileSync(new URL('../www/index.html',import.meta.url),'utf8');
  const composer=fs.readFileSync(new URL('../www/etp-operational-shell-composer.js',import.meta.url),'utf8');
  const bridge=fs.readFileSync(new URL('../www/shared/module-bridge.js',import.meta.url),'utf8');
  const moduleFrame=fs.readFileSync(new URL('../www/modules/etp/index.html',import.meta.url),'utf8');
  const guard=index.indexOf('if(!__DEMO_SEED_ACTIVE) return;');
  const loader=index.indexOf('etp-demo-operational.js');
  assert.match(index,/var DEMO_SEED_ENABLED = false;/);
  assert.ok(guard>=0&&loader>guard);
  assert.equal((index.match(/etp-demo-operational\.js/g)||[]).length,1);
  assert.match(composer,/demo\.syntheticOnly===true/);
  assert.match(bridge,/demo && demo\.syntheticOnly === true/);
  assert.match(moduleFrame,/demoService\.demoSynthetic===true/);
});

test('demo E6 and E7 actions update only synthetic in-memory histories',async()=>{
  const before=demo.models.e6();
  const id=before.exceptions[0].id;
  await demo.models.e6();
  await demo.e7Facade.verify({});
  const run=demo.models.e7().state.runs[0];
  const discrepancy=run.result.discrepancies[0].discrepancyId;
  await demo.e7Facade.addEvidence({discrepancyId:discrepancy,runOperationId:run.operationId,actorId:'DEMO-OWNER',actorRole:'Owner'});
  await demo.e7Facade.close({discrepancyId:discrepancy,runOperationId:run.operationId,actorId:'DEMO-OWNER',actorRole:'Owner'});
  const after=demo.models.e7();
  assert.equal(after.state.evidence.length,1);
  assert.equal(after.state.closures.length,1);
  assert.ok(e7.cleanModel(after));
  assert.match(id,/DEMO-CRO-01/);
});
