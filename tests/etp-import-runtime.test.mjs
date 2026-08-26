import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import crypto from 'node:crypto';
import { createRequire } from 'node:module';
const require=createRequire(import.meta.url);
const runtimeApi=require('../www/etp-import-runtime.js');
const lifecycle=require('../www/etp-store-lifecycle-policy.js');
const coordinator=require('../www/etp-import-coordinator.js');
const reconciliation=require('../www/etp-reconciliation-policy.js');
const profile=require('../www/etp-retail-profile.js');
const nativeApi=require('../www/etp-native-store.js');
const core=require('../www/etp-core-contract.js');
const registryApi=require('../www/etp-control-registry.js');
const readerApi=require('../www/etp-verified-reader.js');
const profileAuthority=require('../www/etp-profile-authority.js');
const importHistoryApi=require('../www/etp-import-history.js');
const tenderDictionaryApi=require('../www/etp-tender-dictionary.js');

const scope={storeCode:'WLMHW',financialYear:'2026-27',periodStart:'2026-04-01',periodEnd:'2026-04-30'};
function loaded(id){
  const common={transactionTypeRaw:'INV',storeCode:'WLMHW',invoiceNumber:'INV-1',invoiceDate:'20260415'};
  const fields=id==='R022'?{...common,invoiceQuantity:'1.000',netValue:'10.00'}:id==='R025'?{...common,itemNumber:'ITEM-1',quantity:'1.000',netAmount:'10.00',netValue:'11.80'}:id==='R013'?{...common,itemNumber:'ITEM-1',croNumber:'CRO-1',quantity:'1.000',netAmount:'9.00',netValue:'10.00'}:{...common,itemNumber:'ITEM-1',quantity:'1.000',netAmount:'9.00',netValue:'10.00'};
  return {ok:true,reportId:id,storeCode:'WLMHW',profileVersion:profileAuthority.PROFILE_VERSION,parserVersion:profileAuthority.PARSER_VERSION,signatureKey:'signature-'+id,rows:[{businessDate:'2026-04-15',fields}]};
}
function harness(loaderOverride){
  const calls=[],stagedChunks=[];
  const nativeApi={create:()=>({ok:true,adapter:{readStatus:async()=>({ok:true,status:{state:'EMPTY',activeGenerationId:null,restoreFence:false}}),beginStage:async()=>{calls.push('begin');return {ok:true};},appendChunk:async(_lifecycle,chunk)=>{calls.push('append');stagedChunks.push(structuredClone(chunk));return {ok:true};},finishStage:async()=>{calls.push('finish');return {ok:true};},publish:async()=>{calls.push('publish');return {ok:true};}}})};
  const values=new Map(),storage={getItem:k=>values.get(k)||null,setItem:(k,v)=>values.set(k,v)};
  const made=runtimeApi.create({profile,profileAuthority,importHistoryApi,tenderDictionaryApi,loader:loaderOverride||{load:async input=>loaded(input.selectedReportId)},testOnlySynchronousParser:true,lifecyclePolicy:lifecycle,coordinatorApi:coordinator,nativeApi,reconciliationPolicy:reconciliation,coreContract:core,controlRegistryApi:registryApi,verifiedReaderApi:readerApi,storage,authorizePublication:async()=>true,plugin:{},crypto:crypto.webcrypto,datePolicy:{earliestDate:'2024-04-01',asOfDate:'2026-08-08',maxFutureDays:2}});
  assert.equal(made.ok,true);return {runtime:made.runtime,calls,stagedChunks,storage};
}
function request(){return {scope,files:['R003','R013','R022','R025'].map(id=>({selectedReportId:id,file:{name:id+'.xlsx',arrayBuffer:async()=>new TextEncoder().encode(id).buffer}})),coverageDeclaration:{confirmed:true,confirmedByRole:'OWNER',reports:Object.fromEntries(['R003','R013','R022','R025'].map(id=>[id,{status:'COMPLETE'}]))},confirmed:false};}

test('browser facade completes four-report parse, validation, reconciliation, staging and confirmation',async()=>{
  const h=harness(),checked=await h.runtime.run(request());
  assert.equal(checked.ok,true,JSON.stringify(checked));assert.equal(checked.awaitingConfirmation,true);
  assert.deepEqual(h.calls,['begin','append','append','append','append','finish']);
  const published=await h.runtime.confirm(checked.lifecycle);
  assert.equal(published.ok,true);assert.equal(published.lifecycle.state,'ACCEPTED');assert.equal(h.calls.at(-1),'publish');
  assert.equal(published.receipt.ruleVersion,'rec_002_v1');
  assert.equal(published.receipt.profileAuthority.evidenceIdentity,'WLMHW_PROFILE_EVIDENCE_2026_08_24_V1');
  assert.deepEqual(published.receipt.tenderDictionary,tenderDictionaryApi.BUILD_IDENTITY);
  const history=JSON.parse(h.storage.getItem(importHistoryApi.KEY));
  assert.deepEqual(history.events.map(event=>event.outcome).sort(),['ACCEPTED','VALIDATED']);
  assert.equal(history.events.every(event=>event.actorId==='BUILD_AUTHORIZED_OWNER'&&event.digestRefs.length===4),true);
  assert.doesNotMatch(JSON.stringify(history),/filename|fileLabel|workbook|rows|customer|mobile|privateBytes/i);
});

test('monthly workbooks merge into one bounded report generation',async()=>{
  const h=harness(),value=request();
  value.files=value.files.flatMap(item=>[item,{selectedReportId:item.selectedReportId,file:{name:item.selectedReportId+'-02.xlsx',arrayBuffer:async()=>new TextEncoder().encode(item.selectedReportId+'-02').buffer}}]);
  const checked=await h.runtime.run(value);
  assert.equal(checked.ok,true,JSON.stringify(checked));
  assert.equal(checked.reconciliation.scopeSelection.sourceRows,8);
  assert.equal(checked.reconciliation.scopeSelection.selectedRows,8);
  assert.equal(h.stagedChunks.length,4);
  assert.equal(h.stagedChunks.every(chunk=>chunk.rows.length===2),true);
});

test('large real-world rows split below the native encrypted chunk byte ceiling',async()=>{
  const h=harness({load:async input=>{const value=loaded(input.selectedReportId),rows=[];for(let i=0;i<500;i++){const row=structuredClone(value.rows[0]);row.fields.invoiceNumber='INV-'+i;row.fields.activationDetails='x'.repeat(2000);rows.push(row);}value.rows=rows;return value;}});
  const checked=await h.runtime.run(request());
  assert.equal(checked.ok,true,JSON.stringify(checked));
  assert.ok(h.stagedChunks.length>4);
  for(const chunk of h.stagedChunks){
    assert.ok(chunk.rows.length<=500);
    assert.ok(Buffer.byteLength(JSON.stringify(chunk.rows),'utf8')<=480*1024);
  }
});

test('precise numeric identifier refusal is surfaced without native staging',async()=>{
  const h=harness({load:async()=>({ok:false,code:'XLSX_IDENTIFIER_NUMERIC_UNVERIFIED'})});
  const result=await h.runtime.run(request());
  assert.equal(result.code,'XLSX_IDENTIFIER_NUMERIC_UNVERIFIED');
  assert.equal(result.coordinatorCode,'ETP_PARSE_REJECTED');
  assert.deepEqual(h.calls,[]);
});

test('non-zero unresolved PAYMENTTYPE25 is excluded and recorded as quarantine metadata',async()=>{
  const h=harness({load:async input=>{const value=loaded(input.selectedReportId);if(input.selectedReportId==='R022')value.rows[0].fields.paymentType25Amount='1.00';return value;}}),checked=await h.runtime.run(request());
  assert.equal(checked.ok,true);const result=await h.runtime.confirm(checked.lifecycle);assert.equal(result.receipt.enrichments.paymentType25.rowCount,1);assert.equal(result.receipt.enrichments.paymentType25.persisted,false);
});

test('multi-financial-year workbooks publish only the explicit one-year scope',async()=>{
  const h=harness({load:async input=>{
    const value=loaded(input.selectedReportId), outside=structuredClone(value.rows[0]);
    outside.businessDate='2025-03-31';outside.fields.invoiceDate='20250331';
    if(input.selectedReportId==='R025')outside.fields.netAmount='999.00';
    value.rows.unshift(outside);return value;
  }}),checked=await h.runtime.run(request());
  assert.equal(checked.ok,true,JSON.stringify(checked));
  assert.equal(checked.awaitingConfirmation,true);
  assert.deepEqual(checked.reconciliation.scopeSelection,{
    mode:'EXPLICIT_SCOPE_FILTER',sourceRows:8,selectedRows:4,excludedRows:4,
    reports:Object.fromEntries(['R003','R013','R022','R025'].map(id=>[id,{sourceRows:2,selectedRows:1,excludedRows:1}]))
  });
  assert.equal(checked.reconciliation.status,'PASS','out-of-scope economics must not enter selected-scope reconciliation');
  assert.equal(h.stagedChunks.length,4);
  for(const chunk of h.stagedChunks){
    assert.equal(chunk.scopeKey,'WLMHW|2026-27|2026-04-01..2026-04-30');
    assert.ok(chunk.rows.length>0);
    for(const row of chunk.rows){
      assert.equal(row.invoice_date,'20260415');
      assert.notEqual(row.net_amount,'999.00');
    }
  }
});

test('scope filtering includes both period boundaries and excludes adjacent dates',async()=>{
  const h=harness({load:async input=>{
    const value=loaded(input.selectedReportId),template=value.rows[0];
    function at(date){const row=structuredClone(template);row.businessDate=date;row.fields.invoiceDate=date.replace(/-/g,'');return row;}
    value.rows=[at('2026-03-31'),at('2026-04-01'),at('2026-04-30'),at('2026-05-01')];
    return value;
  }}),checked=await h.runtime.run(request());
  assert.equal(checked.ok,true,JSON.stringify(checked));
  assert.equal(checked.reconciliation.scopeSelection.sourceRows,16);
  assert.equal(checked.reconciliation.scopeSelection.selectedRows,8);
  assert.equal(checked.reconciliation.scopeSelection.excludedRows,8);
  for(const chunk of h.stagedChunks){
    assert.deepEqual(chunk.rows.map(row=>row.invoice_date),['20260401','20260430']);
  }
});

test('only in-scope PAYMENTTYPE25 rows contribute to quarantine metadata',async()=>{
  const h=harness({load:async input=>{
    const value=loaded(input.selectedReportId);
    if(input.selectedReportId==='R022'){
      value.rows[0].fields.paymentType25Amount='2.00';
      const outside=structuredClone(value.rows[0]);outside.businessDate='2025-03-31';outside.fields.invoiceDate='20250331';outside.fields.paymentType25Amount='99.00';value.rows.push(outside);
    }
    return value;
  }}),checked=await h.runtime.run(request());
  assert.equal(checked.ok,true,JSON.stringify(checked));
  const published=await h.runtime.confirm(checked.lifecycle);
  assert.equal(published.receipt.enrichments.paymentType25.rowCount,1);
});

test('a selected scope with no rows fails before native staging',async()=>{
  const h=harness({load:async input=>{const value=loaded(input.selectedReportId);value.rows[0].businessDate='2025-03-31';value.rows[0].fields.invoiceDate='20250331';return value;}}),result=await h.runtime.run(request());
  assert.equal(result.code,'ETP_SELECTED_SCOPE_HAS_NO_ROWS');
  assert.equal(result.coordinatorCode,'ETP_POLICY_REJECTED');
  assert.deepEqual(h.calls,[]);
});

test('HEMW production is denied before file reads, native status or staging',async()=>{
  let fileReads=0,loaderCalls=0,nativeCalls=0;
  const deniedNative={create:()=>({ok:true,adapter:{readStatus:async()=>{nativeCalls++;return{ok:true,status:{state:'EMPTY',activeGenerationId:null,restoreFence:false}};},beginStage:async()=>{nativeCalls++;},appendChunk:async()=>{nativeCalls++;},finishStage:async()=>{nativeCalls++;},publish:async()=>{nativeCalls++;}}})};
  const storage={getItem:()=>null,setItem:()=>{}};
  const made=runtimeApi.create({profile,profileAuthority,importHistoryApi,tenderDictionaryApi,loader:{load:async()=>{loaderCalls++;return{ok:false};}},testOnlySynchronousParser:true,lifecyclePolicy:lifecycle,coordinatorApi:coordinator,nativeApi:deniedNative,reconciliationPolicy:reconciliation,coreContract:core,controlRegistryApi:registryApi,verifiedReaderApi:readerApi,storage,authorizePublication:async()=>true,plugin:{},crypto:crypto.webcrypto,datePolicy:{earliestDate:'2024-04-01',asOfDate:'2026-08-08',maxFutureDays:2}});
  const heRequest=request();heRequest.scope={...scope,storeCode:'HEMW'};heRequest.files=heRequest.files.map(item=>({...item,file:{name:item.file.name,arrayBuffer:async()=>{fileReads++;return new ArrayBuffer(1);}}}));
  const result=await made.runtime.run(heRequest);assert.equal(result.code,'ETP_HEMW_PROFILE_AUTHORIZATION_REQUIRED');assert.equal(fileReads,0);assert.equal(loaderCalls,0);assert.equal(nativeCalls,0);
});

test('parsed profile or parser version mismatch fails before native staging',async()=>{for(const field of ['profileVersion','parserVersion']){const h=harness({load:async input=>({...loaded(input.selectedReportId),[field]:'stale'})}),result=await h.runtime.run(request());assert.equal(result.code,'ETP_PROFILE_VERSION_MISMATCH');assert.deepEqual(h.calls,[]);}});

test('shell loads pinned local bundles and runtime dependencies before the governed gateway',()=>{
  const shell=fs.readFileSync(new URL('../www/index.html',import.meta.url),'utf8');
  const order=['vendor/fflate-0.8.3.min.js','vendor/read-excel-file-9.3.7.min.js','etp-import-foundation.js','etp-xlsx-preflight.js','etp-profile-authority.js','etp-retail-xlsx-loader.js','etp-core-contract.js','etp-native-store.js','etp-control-registry.js','etp-verified-reader.js','etp-worker-client.js','etp-import-coordinator.js','etp-import-runtime.js','etp-module-gateway.js'].map(src=>shell.indexOf(`<script src="${src}"></script>`));
  assert.ok(order.every(at=>at>=0));assert.deepEqual(order,order.slice().sort((a,b)=>a-b));
  assert.doesNotMatch(shell.slice(order[0],order.at(-1)),/https?:\/\//);
  assert.doesNotMatch(shell,/<script src="etp-import-ui\.js"><\/script>/);
});

test('staged browser assets exactly match pinned installed dependency bytes',()=>{
  assert.deepEqual(fs.readFileSync(new URL('../www/vendor/read-excel-file-9.3.7.min.js',import.meta.url)),fs.readFileSync(new URL('../node_modules/read-excel-file/bundle/read-excel-file.min.js',import.meta.url)));
  assert.deepEqual(fs.readFileSync(new URL('../www/vendor/fflate-0.8.3.min.js',import.meta.url)),fs.readFileSync(new URL('../node_modules/fflate/umd/index.js',import.meta.url)));
});

test('facade compiles a native-safe fact dictionary from the complete Retail profile',()=>{
  const storage={getItem:()=>null,setItem:()=>{}};
  const made=runtimeApi.create({profile,profileAuthority,importHistoryApi,tenderDictionaryApi,loader:{load:async()=>({ok:false})},testOnlySynchronousParser:true,lifecyclePolicy:lifecycle,coordinatorApi:coordinator,nativeApi,reconciliationPolicy:reconciliation,coreContract:core,controlRegistryApi:registryApi,verifiedReaderApi:readerApi,storage,authorizePublication:async()=>true,plugin:{},crypto:crypto.webcrypto,datePolicy:{earliestDate:'2024-04-01',asOfDate:'2026-08-08',maxFutureDays:2}});
  assert.equal(made.ok,true);
});
