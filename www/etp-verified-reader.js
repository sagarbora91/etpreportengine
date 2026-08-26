/* UI-independent verified fact gateway. Raw native reads are never exposed to
   business screens without an active receipt, passed reconciliation and scope. */
(function(root,factory){var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpVerifiedReader=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  function snake(v){return String(v).replace(/([a-z0-9])([A-Z])/g,'$1_$2').toLowerCase();}
  function create(options){var core=options&&options.core,profile=options&&options.profile,nativeStore=options&&options.nativeStore,registry=options&&options.registry;if(!core||!profile||!nativeStore||!registry)return{ok:false,code:'ETP_READER_DEPENDENCY_INVALID'};
    var fields={};core.REPORTS.forEach(function(id){fields[id]={};Object.keys(profile.REPORTS[id].fields).forEach(function(raw){var field=snake(profile.REPORTS[id].fields[raw]);if(field!=='payment_type25_amount')fields[id][field]=true;});});
    async function read(scope,request){var checkedScope=options.lifecyclePolicy.validateScope(scope);if(!checkedScope.ok)return{ok:false,code:'ETP_SCOPE_INVALID'};var report=String(request&&request.reportId||'').toUpperCase(),requested=request&&request.fields;if(core.REPORTS.indexOf(report)<0||!Array.isArray(requested)||!requested.length||requested.some(function(f){return !fields[report][String(f)];}))return{ok:false,code:'ETP_VERIFIED_PROJECTION_INVALID'};var found=registry.getReceipt(checkedScope.key);if(!found.ok)return found;var receipt=found.receipt,status=await nativeStore.readStatus(scope);if(!status.ok)return status;if(status.status.state!=='ACCEPTED'||status.status.restoreFence||status.status.activeGenerationId!==receipt.activeGenerationId)return{ok:false,code:'ETP_VERIFIED_GENERATION_UNAVAILABLE'};return nativeStore.readFacts(scope,{generationId:receipt.activeGenerationId,reportId:report,fields:requested,cursor:request.cursor||null,limit:request.limit});}
    return{ok:true,reader:Object.freeze({read:read})};
  }
  return Object.freeze({VERSION:1,create:create});
});
