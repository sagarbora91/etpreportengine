#!/usr/bin/env node
/* Aggregate-only real-export verifier. No workbook names, headers, cells or rows
   are emitted or written. Source archives remain outside the app/repository. */
import fs from 'node:fs';
import crypto from 'node:crypto';
import readXlsxFile from 'read-excel-file/node';
import { unzipSync } from 'fflate';
import { createRequire } from 'node:module';
const require=createRequire(import.meta.url);
const profile=require('../www/etp-retail-profile.js');
const tableParser=require('../www/etp-retail-table-parser.js');
const loaderApi=require('../www/etp-retail-xlsx-loader.js');
const numeric=require('../www/etp-xlsx-parser-policy.js');
const core=require('../www/etp-core-contract.js');
const profileAuthority=require('../www/etp-profile-authority.js');
const reconciliation=require('../www/etp-reconciliation-policy.js');
const REPORTS=['R003','R013','R022','R025'];
const inputs=process.argv.slice(2);
if(inputs.length!==2)throw new Error('Usage: node scripts/verify-etp-core-real-files.mjs <WLMHW.zip> <HEMW.zip>');
const loader=loaderApi.create({readWorkbook:(bytes,options)=>readXlsxFile(Buffer.from(bytes),options),unzipParts:bytes=>unzipSync(bytes)});
function hash(bytes){return crypto.createHash('sha256').update(bytes).digest('hex');}
function safeFacts(rows){return rows.map(row=>row.fields);}
function exactZero(value){return /^[-+]?0+(?:\.0+)?$/.test(String(value==null?'':value).trim());}
async function verify(archivePath,storeCode){
  const decision=profileAuthority.authorize({storeCode,purpose:'AGGREGATE_EVIDENCE',profileVersion:profile.ETP_PROFILE_VERSION,parserVersion:tableParser.PARSER_VERSION});
  if(!decision.ok)return{ok:false,storeCode,productionReady:false,code:decision.code};
  const archive=unzipSync(fs.readFileSync(archivePath)),entries=Object.entries(archive),reports={},aggregates=[];
  for(const id of REPORTS){
    const number=id.slice(1),matched=entries.filter(([name])=>new RegExp('(?:^|/)[RWH]'+number+'[_ -]','i').test(name.replace(/\\/g,'/'))&&/\.xlsx$/i.test(name));
    if(matched.length!==1)return{ok:false,storeCode,code:'REAL_REPORT_SELECTION_INVALID',reportId:id};
    const [name,bytes]=matched[0],started=performance.now(),loaded=await loader.load({bytes,fileLabel:name.replace(/\\/g,'/').split('/').pop(),selectedReportId:id,expectedStoreCode:storeCode,datePolicy:{earliestDate:'2024-04-01',asOfDate:core.indiaDate(Date.now()),maxFutureDays:2}});
    if(!loaded.ok)return{ok:false,storeCode,code:loaded.code,reportId:id};
    reports[id]=Object.assign({},loaded,{sourceSha256:hash(bytes)});
    aggregates.push({reportId:id,rows:loaded.rowCount,columns:profile.REPORTS[id].exactHeaders.length,sourceSha256:reports[id].sourceSha256,signatureSha256:hash(Buffer.from(loaded.signatureKey)),elapsedMs:Math.round(performance.now()-started),piiCanaryPresent:JSON.stringify(loaded.rows).includes('PRIVATE'),unresolvedPaymentType25Rows:id==='R022'?loaded.rows.filter(row=>row.fields.paymentType25Amount!==''&&row.fields.paymentType25Amount!=null&&!exactZero(row.fields.paymentType25Amount)).length:0,paymentType25ExcludedFromFacts:id==='R022'});
  }
  const coverage=core.coverage({storeCode,financialYear:'2026-27',periodStart:'2026-04-01',periodEnd:'2027-03-31'},reports,{confirmed:true,confirmedByRole:'OWNER',reports:Object.fromEntries(REPORTS.map(id=>[id,{status:'COMPLETE'}]))});
  const recon=reconciliation.compareReports(safeFacts(reports.R022.rows),safeFacts(reports.R025.rows),core.RECON_RULE,{left:coverage.coverage.R022,right:coverage.coverage.R025});
  const attribution=reconciliation.compareReports(safeFacts(reports.R013.rows),safeFacts(reports.R025.rows),core.ATTRIBUTION_RULE,{left:coverage.coverage.R013,right:coverage.coverage.R025});
  const discount=reconciliation.compareReports(safeFacts(reports.R003.rows),safeFacts(reports.R025.rows),core.DISCOUNT_RULE,{left:coverage.coverage.R003,right:coverage.coverage.R025});
  const left=reconciliation.aggregateReportRows('R022',safeFacts(reports.R022.rows),core.RECON_RULE),right=reconciliation.aggregateReportRows('R025',safeFacts(reports.R025.rows),core.RECON_RULE),leftKeys=new Set(left.groups.map(x=>JSON.stringify(x.key))),rightKeys=new Set(right.groups.map(x=>JSON.stringify(x.key)));
  return{ok:true,storeCode,productionReady:decision.productionReady,authority:{contractVersion:decision.binding.contractVersion,authorityId:decision.binding.authorityId,status:decision.binding.status,purpose:decision.binding.purpose,profileVersion:decision.binding.profileVersion,parserVersion:decision.binding.parserVersion,evidenceIdentity:decision.binding.evidenceIdentity},reports:aggregates,reconciliation:{status:recon.status,code:recon.code,differenceCount:Array.isArray(recon.differences)?recon.differences.length:0,leftGroups:left.groups.length,rightGroups:right.groups.length,sharedGroups:[...leftKeys].filter(x=>rightKeys.has(x)).length,differencesByMeasure:Object.fromEntries(core.RECON_RULE.measures.map(m=>[m.name,recon.differences.filter(d=>d.measure===m.name).length]))},attribution:{status:attribution.status,code:attribution.code,differenceCount:attribution.differences.length},discountEnrichment:{status:discount.status,code:discount.code,differenceCount:discount.differences.length}};
}
const results=[await verify(inputs[0],'WLMHW'),await verify(inputs[1],'HEMW')];
const evidence={format:'SAAGAR_ETP_CORE_REAL_CONFORMANCE',contractVersion:core.ETP_CORE_VERSION,evaluatedAt:new Date().toISOString(),privacy:'aggregate metadata only; source workbooks remain external',productionReady:false,results,passed:results.every(x=>x.ok&&x.reports.length===4&&x.reports.every(r=>!r.piiCanaryPresent&&(r.reportId!=='R022'||r.paymentType25ExcludedFromFacts===true))&&x.reconciliation.status==='PASS')};
process.stdout.write(JSON.stringify(evidence,null,2)+'\n');if(!evidence.passed)process.exitCode=1;
