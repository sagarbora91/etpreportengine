/* V6 ETP E5: one-way controlled Payroll earning-line bridge; no Payroll editor capability. */
(function(root,factory){'use strict';var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpE5PayrollBridge=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VERSION='ETP_E5_PAYROLL_BRIDGE_V1',MAX_MONEY=900000000000;
  function freeze(v){if(v&&typeof v==='object'&&!Object.isFrozen(v)){Object.keys(v).forEach(function(k){freeze(v[k]);});Object.freeze(v);}return v;}
  function rec(v){return!!v&&typeof v==='object'&&!Array.isArray(v);}
  function fail(code){return freeze({ok:false,code:code});}
  function id(v,re){return typeof v==='string'&&re.test(v)?v:null;}
  function money(v){return Number.isSafeInteger(v)&&Math.abs(v)<=MAX_MONEY?v:null;}
  function createEarningLine(input){var f=input&&input.finalized,p=input&&input.policyLine,c=input&&input.clawbacks,ids=[],sum=0,i,lineId;
    if(!rec(input)||!rec(f)||f.contractVersion!=='ETP_E5_INCENTIVE_V1'||f.status!=='FINALIZED'||f.mode!=='FINAL'||f.source!=='ETP_VERIFIED'||!id(f.operationId,/^E5-[a-f0-9]{8}$/)||!id(f.scopeKey,/^(?:WLMHW|HEMW)\|[^|]+\|\d{4}-\d{2}-\d{2}\.\.\d{4}-\d{2}-\d{2}$/)||!id(f.generationId,/^etp_[a-f0-9]{32}$/)||!id(f.receiptId,/^[A-Za-z0-9][A-Za-z0-9._:-]{2,95}$/)||!id(f.employeeId,/^[A-Za-z0-9][A-Za-z0-9._:-]{2,95}$/)||!id(f.period,/^\d{4}-\d{2}$/)||money(f.incentive)===null||!rec(p)||p.lineType!=='CONTROLLED_ETP_INCENTIVE'||p.editable!==false||p.recreatable!==false||p.sourceOperationId!==f.operationId||p.preLockGate!=='REQUIRED'||p.sumIdentity!==true||money(p.amount)===null||!Array.isArray(c)||c.length>120)return fail('E5_PAYROLL_BRIDGE_BLOCKED');
    for(i=0;i<c.length;i++){if(!rec(c[i])||!id(c[i].clawbackId,/^E5-CLAW-[a-f0-9]{8}$/)||c[i].finalOperationId!==f.operationId||c[i].status!=='PENDING_PAYROLL'||money(c[i].amount)===null||ids.indexOf(c[i].clawbackId)>=0)return fail('E5_PAYROLL_BRIDGE_BLOCKED');ids.push(c[i].clawbackId);sum+=c[i].amount;if(money(sum)===null)return fail('E5_PAYROLL_BRIDGE_BLOCKED');}
    ids.sort();if(p.baseAmount!==f.incentive||p.clawbackAmount!==sum||p.amount!==f.incentive+sum)return fail('E5_PAYROLL_BRIDGE_BLOCKED');lineId='ETP-EARNING-'+f.operationId;return freeze({ok:true,line:freeze({contractVersion:VERSION,lineId:lineId,lineType:'CONTROLLED_ETP_INCENTIVE',scopeKey:f.scopeKey,generationId:f.generationId,receiptId:f.receiptId,period:f.period,employeeId:f.employeeId,sourceOperationId:f.operationId,schemeVersion:f.schemeVersion,authorityPackageSha256:f.authorityPackageSha256,clawbackIds:freeze(ids),amount:p.amount,status:'PAYROLL_ATTACHED',editable:false,recreatable:false,manualEntryAllowed:false,basis:'ETP_VERIFIED',preLockGate:'REQUIRED'})});
  }
  return freeze({VERSION:VERSION,createEarningLine:createEarningLine});
});
