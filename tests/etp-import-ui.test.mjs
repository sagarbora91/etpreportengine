import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRequire } from 'node:module';
const require=createRequire(import.meta.url);
const api=require('../www/etp-import-ui.js');
const source=fs.readFileSync(new URL('../www/etp-import-ui.js',import.meta.url),'utf8');
const shell=fs.readFileSync(new URL('../www/index.html',import.meta.url),'utf8');

function ready(ui){ui.setScope('storeCode','HEMW');ui.setScope('financialYear','2026-27');ui.setScope('periodStart','2026-04-01');ui.setScope('periodEnd','2026-04-30');ui.setCoverageConfirmed(true);for(const id of api.REPORTS)ui.setFile(id,{name:id+'.xlsx'});}

test('UI requires one exact file for all four reports and fails closed without runtime',async()=>{
  const ui=api.create();
  assert.deepEqual(api.REPORTS,['R003','R013','R022','R025']);
  assert.equal((await ui.start()).code,'ETP_SELECTION_INCOMPLETE');
  ready(ui);delete globalThis.SaagarEtpImportRuntime;
  assert.equal((await ui.start()).code,'ETP_RUNTIME_UNAVAILABLE');
});

test('scope registry is written only after verified publication',async()=>{
  const values=new Map();globalThis.localStorage={getItem:k=>values.get(k)||null,setItem:(k,v)=>values.set(k,v)};
  globalThis.SaagarEtpImportRuntime={
    run:async()=>({ok:true,awaitingConfirmation:true,lifecycle:{state:'AWAITING_CONFIRMATION'}}),
    confirm:async()=>({ok:true,lifecycle:{state:'ACCEPTED'}})
  };
  const ui=api.create();ready(ui);
  assert.equal((await ui.start()).awaitingConfirmation,true);
  assert.equal(values.has(api.REGISTRY_KEY),false);
  await ui.confirm();
  assert.deepEqual(JSON.parse(values.get(api.REGISTRY_KEY)),[{scopeKey:'HEMW|2026-27|2026-04-01..2026-04-30'}]);
});

test('responsive source has a desktop overview and mobile stacked controls with no business totals',()=>{
  assert.match(source,/grid-template-columns:230px minmax\(0,850px\)/);
  assert.match(source,/@media\(max-width:699px\)/);
  assert.match(source,/\.etp-grid,\.etp-files\{grid-template-columns:1fr\}/);
  assert.match(source,/No sales totals are shown here/);
  assert.match(source,/complete selected period/);
  assert.doesNotMatch(source,/₹|salesTotal|revenueTotal/);
});

test('R013 and R003 exception presentation is exact, bounded and explicitly non-revenue',()=>{
  const reconciliation={enrichments:{
    R013:{status:'FAIL',differenceCount:33},
    R003:{status:'PASS',differenceCount:0},
    paymentType25:{status:'QUARANTINED',rowCount:7,persisted:false}
  }};
  assert.deepEqual(api.exceptionPresentation({reconciliation}),[
    {reportId:'R013',label:'CRO attribution',help:'Compare CRO-attributed lines with sales-detail lines.',status:'FAIL',differenceCount:33},
    {reportId:'R003',label:'Discount lines',help:'Compare discount lines with sales-detail lines.',status:'PASS',differenceCount:0}
  ]);
  assert.deepEqual(api.exceptionPresentation({receipt:{enrichments:reconciliation.enrichments}}),api.exceptionPresentation({reconciliation}));
  assert.deepEqual(api.exceptionPresentation({reconciliation:{enrichments:{R013:{status:'FAIL',differenceCount:250001},R003:{status:'UNKNOWN',differenceCount:1}}}}),[]);
  assert.match(source,/R003\/R013 report exceptions/);
  assert.match(source,/These checks do not change revenue or sales totals/);
  assert.match(source,/Review open differences before using CRO attribution or discount analysis/);
});

test('shell exposes the dedicated Reports-owned ETP module route through the governed gateway only',()=>{
  assert.match(shell,/<script src="etp-import-runtime\.js"><\/script>\s*<script src="etp-verified-analytics\.js"><\/script>[\s\S]*?<script src="etp-incentive-control\.js"><\/script>\s*<script src="etp-module-gateway\.js"><\/script>\s*<script src="etp-analytics-consumer\.js"><\/script>\s*<script src="etp-operations-consumer\.js"><\/script>/);
  assert.doesNotMatch(shell,/<script src="etp-import-ui\.js"><\/script>/);
  const reports=shell.slice(shell.indexOf('id="reportsView"'),shell.indexOf('id="configView"'));
  const settings=shell.slice(shell.indexOf('id="configView"'),shell.indexOf('</main>'));
  assert.match(reports,/id="reportsEtpImportCard"[\s\S]*navigateToModule\('etp'\)[\s\S]*Open Retail ETP/);
  assert.doesNotMatch(reports,/SaagarEtpImportUi\.open/);
  assert.match(reports,/exact R003, R013, R022 and R025 exports/);
  assert.doesNotMatch(settings,/Retail ETP reports|Open Retail ETP|navigateToModule\('etp'\)/);
});
