/* Versioned, metadata-only readiness semantics for the bounded Retail ETP
   foundation. This contract accepts no workbook material, filenames or facts. */
(function(root,factory){
  'use strict';
  var api=factory();
  if(typeof module==='object'&&module.exports)module.exports=api;
  if(root)root.SaagarEtpFoundationStatus=api;
})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VERSION='ETP_FOUNDATION_V1';
  var REPORTS=Object.freeze(['R003','R013','R022','R025']);
  var STATUSES=Object.freeze(['READY','READY_WITH_WARNINGS','NOT_READY']);
  var BLOCKED=Object.freeze(['__proto__','prototype','constructor']);

  function freeze(value){return Object.freeze(value);}
  function plain(value){if(!value||typeof value!=='object'||Array.isArray(value))return false;var proto=Object.getPrototypeOf(value);return proto===Object.prototype||proto===null;}
  function own(value,key){return Object.prototype.hasOwnProperty.call(value,key);}
  function exact(value,keys){if(!plain(value))return false;var actual=Object.keys(value);if(actual.length!==keys.length)return false;for(var i=0;i<actual.length;i++)if(BLOCKED.indexOf(actual[i])>=0||keys.indexOf(actual[i])<0)return false;return true;}
  function iso(value){var raw=String(value||'');if(!/^\d{4}-\d{2}-\d{2}$/.test(raw))return'';var parts=raw.split('-'),date=new Date(raw+'T00:00:00Z');return !Number.isNaN(date.getTime())&&date.getUTCFullYear()===Number(parts[0])&&date.getUTCMonth()+1===Number(parts[1])&&date.getUTCDate()===Number(parts[2])?raw:'';}
  function timestamp(value){var raw=String(value||'');if(!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z$/.test(raw))return'';var time=Date.parse(raw);return Number.isFinite(time)?new Date(time).toISOString():'';}
  function financialYear(value){var match=/^(\d{4})-(\d{2})$/.exec(String(value||''));return match&&Number(match[2])===(Number(match[1])+1)%100?match[0]:'';}
  function yearOf(date){var year=Number(date.slice(0,4)),start=date.slice(5,7)>='04'?year:year-1,end=String((start+1)%100);return String(start)+'-'+(end.length<2?'0'+end:end);}
  function generation(value){return /^etp_[a-f0-9]{32}$/.test(String(value||''));}
  function safeVersion(value){return /^[A-Za-z][A-Za-z0-9._:-]{0,79}$/.test(String(value||''));}
  function uniqueSorted(values){return freeze(values.filter(function(value,index,all){return all.indexOf(value)===index;}).sort());}
  function fail(){return freeze({ok:false,code:'ETP_FOUNDATION_INPUT_INVALID',contractVersion:VERSION});}

  function scope(value){
    if(!exact(value,['storeCode','financialYear','periodStart','periodEnd','scopeKey']))return null;
    var store=String(value.storeCode||''),fy=financialYear(value.financialYear),start=iso(value.periodStart),end=iso(value.periodEnd);
    if(['WLMHW','HEMW'].indexOf(store)<0||!fy||!start||!end||start>end||yearOf(start)!==fy||yearOf(end)!==fy)return null;
    var key=[store,fy,start+'..'+end].join('|');
    return value.scopeKey===key?freeze({storeCode:store,financialYear:fy,periodStart:start,periodEnd:end,scopeKey:key}):null;
  }
  function nativeStatus(value){
    if(!exact(value,['state','restoreFence','activeGenerationId'])||['EMPTY','STAGING','ACCEPTED','REIMPORT_REQUIRED'].indexOf(value.state)<0||typeof value.restoreFence!=='boolean')return null;
    if(value.activeGenerationId!==null&&!generation(value.activeGenerationId))return null;
    return freeze({state:value.state,restoreFence:value.restoreFence,activeGenerationId:value.activeGenerationId});
  }
  function coverage(value){
    if(!plain(value)||Object.keys(value).sort().join('|')!==REPORTS.slice().sort().join('|'))return null;
    var out={},valid=true;
    REPORTS.forEach(function(id){var item=value[id];if(!exact(item,['status','zeroActivityConfirmed'])||['COMPLETE','COMPLETE_WITH_ZERO_ACTIVITY'].indexOf(item.status)<0||typeof item.zeroActivityConfirmed!=='boolean'||(item.status==='COMPLETE_WITH_ZERO_ACTIVITY'&&item.zeroActivityConfirmed!==true)||(item.status==='COMPLETE'&&item.zeroActivityConfirmed!==false)){valid=false;return;}out[id]=freeze({status:item.status,zeroActivityConfirmed:item.zeroActivityConfirmed});});
    return valid?freeze(out):null;
  }
  function exceptions(value){
    if(!exact(value,['R003','R013','paymentType25']))return null;
    var out={},valid=true;
    ['R003','R013'].forEach(function(id){var item=value[id],count=item&&item.differenceCount;if(!exact(item,['status','differenceCount'])||['PASS','FAIL'].indexOf(item.status)<0||!Number.isSafeInteger(count)||count<0||count>250000){valid=false;return;}out[id]=freeze({status:item.status,differenceCount:count});});
    var payment=value.paymentType25,count=payment&&payment.rowCount;
    if(!exact(payment,['status','rowCount','persisted'])||payment.status!=='QUARANTINED'||!Number.isSafeInteger(count)||count<0||count>250000||payment.persisted!==false)valid=false;
    else out.paymentType25=freeze({status:'QUARANTINED',rowCount:count,persisted:false});
    return valid?freeze(out):null;
  }
  function receipt(value){
    if(value===null)return null;
    if(!exact(value,['activeGenerationId','reconciliationStatus','coverage','exceptions','profileVersion','ruleVersion','publishedAt']))return false;
    var checkedCoverage=coverage(value.coverage),checkedExceptions=exceptions(value.exceptions);
    var publishedAt=timestamp(value.publishedAt);
    if(!generation(value.activeGenerationId)||['PASS','FAIL','BLOCKED'].indexOf(value.reconciliationStatus)<0||!checkedCoverage||!checkedExceptions||!safeVersion(value.profileVersion)||!safeVersion(value.ruleVersion)||!publishedAt)return false;
    return freeze({activeGenerationId:value.activeGenerationId,reconciliationStatus:value.reconciliationStatus,coverage:checkedCoverage,exceptions:checkedExceptions,profileVersion:value.profileVersion,ruleVersion:value.ruleVersion,publishedAt:publishedAt});
  }
  function evaluate(value){
    if(!exact(value,['contractVersion','scope','factStoreAvailable','nativeStatus','receipt'])||value.contractVersion!==VERSION||typeof value.factStoreAvailable!=='boolean')return fail();
    var checkedScope=scope(value.scope),native=nativeStatus(value.nativeStatus),accepted=receipt(value.receipt);
    if(!checkedScope||!native||accepted===false)return fail();
    var reasons=[],warnings=[];
    if(value.factStoreAvailable!==true)reasons.push('FACT_STORE_UNAVAILABLE');
    if(native.restoreFence===true||native.state==='REIMPORT_REQUIRED')reasons.push('REIMPORT_REQUIRED');
    if(native.state!=='ACCEPTED')reasons.push('VERIFIED_GENERATION_UNAVAILABLE');
    if(!accepted)reasons.push('RECEIPT_REQUIRED');
    if(accepted){
      if(native.activeGenerationId!==accepted.activeGenerationId)reasons.push('GENERATION_MISMATCH');
      if(accepted.reconciliationStatus!=='PASS')reasons.push('REC_002_NOT_PASSED');
      REPORTS.forEach(function(id){if(accepted.coverage[id].status==='COMPLETE_WITH_ZERO_ACTIVITY')warnings.push(id+'_ZERO_ACTIVITY_CONFIRMED');});
      ['R003','R013'].forEach(function(id){if(accepted.exceptions[id].status!=='PASS')warnings.push(id+'_EXCEPTIONS_OPEN');});
      if(accepted.exceptions.paymentType25.rowCount>0)warnings.push('PAYMENTTYPE25_QUARANTINED');
    }
    reasons=uniqueSorted(reasons);warnings=uniqueSorted(warnings);
    var status=reasons.length?'NOT_READY':(warnings.length?'READY_WITH_WARNINGS':'READY');
    return freeze({ok:true,contractVersion:VERSION,status:status,showValues:!reasons.length,reasons:reasons,warnings:warnings,scope:checkedScope,activeGenerationId:!reasons.length&&accepted?accepted.activeGenerationId:null,verifiedThrough:!reasons.length&&accepted?checkedScope.periodEnd:null,publishedAt:!reasons.length&&accepted?accepted.publishedAt:null});
  }
  return freeze({VERSION:VERSION,REPORTS:REPORTS,STATUSES:STATUSES,evaluate:evaluate});
});
