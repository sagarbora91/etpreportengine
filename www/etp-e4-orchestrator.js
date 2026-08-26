/* V6 E4: fail-closed orchestration over target policy, authority and injected storage. */
(function(root,factory){'use strict';var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpE4Orchestrator=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VERSION='ETP_E4_ORCHESTRATOR_V1',TYPES=Object.freeze(['TITAN_TARGET','FESTIVE_CALENDAR','CRO_IDENTITY_MAP','E4_POLICY_AUTHORITY']);
  function freeze(v){if(v&&typeof v==='object'&&!Object.isFrozen(v)){Object.keys(v).forEach(function(k){freeze(v[k]);});Object.freeze(v);}return v;}
  function fail(code){return freeze({ok:false,code:code});}
  function record(v){return !!v&&typeof v==='object'&&!Array.isArray(v);}
  function text(v,re){return typeof v==='string'&&re.test(v)?v:'';}
  function time(v){return typeof v==='string'&&/^\d{4}-\d{2}-\d{2}T/.test(v)&&isFinite(Date.parse(v))?new Date(Date.parse(v)).toISOString():'';}
  function exact(v,keys){return record(v)&&Object.keys(v).sort().join('|')===keys.slice().sort().join('|');}
  function scope(v){var m=typeof v==='string'?/^(WLMHW|HEMW)\|([^|]{3,20})\|(\d{4}-\d{2}-\d{2})\.\.(\d{4}-\d{2}-\d{2})$/.exec(v):null;return m&&m[3]<=m[4]?{scopeKey:v,storeCode:m[1],periodId:m[2],periodStart:m[3],periodEnd:m[4]}:null;}
  function binding(v,s){return record(v)&&v.source==='ETP_VERIFIED'&&v.scopeKey===s.scopeKey&&text(v.generationId,/^etp_[a-f0-9]{32}$/)&&text(v.receiptId,/^[A-Za-z0-9][A-Za-z0-9._:-]{2,95}$/)?freeze({source:'ETP_VERIFIED',scopeKey:v.scopeKey,generationId:v.generationId,receiptId:v.receiptId}):null;}
  function sourceItem(v,type,s){
    if(!exact(v,['sourceType','status','sourceSha256','approvalId','approvedAt','approvedByRole','storeCode','scopeKey'])||v.sourceType!==type||v.status!=='APPROVED'||v.approvedByRole!=='Owner'||v.storeCode!==s.storeCode||v.scopeKey!==s.scopeKey||!text(v.sourceSha256,/^[a-f0-9]{64}$/)||!text(v.approvalId,/^[A-Z0-9][A-Z0-9._-]{2,63}$/)||!time(v.approvedAt))return null;
    return freeze({sourceType:type,status:'APPROVED',sourceSha256:v.sourceSha256,approvalId:v.approvalId,approvedAt:time(v.approvedAt),approvedByRole:'Owner',storeCode:s.storeCode,scopeKey:s.scopeKey});
  }
  function sources(v,s){
    if(!record(v)||Object.keys(v).sort().join('|')!==TYPES.slice().sort().join('|'))return null;
    var out={},hashes={},seen={};for(var i=0;i<TYPES.length;i++){var item=sourceItem(v[TYPES[i]],TYPES[i],s);if(!item||seen[item.sourceSha256])return null;seen[item.sourceSha256]=true;out[TYPES[i]]=item;hashes[TYPES[i]]=item.sourceSha256;}
    return freeze({items:out,sourceHashes:hashes});
  }
  function create(deps){
    if(!record(deps)||!record(deps.planning)||typeof deps.planning.publish!=='function'||typeof deps.planning.compute!=='function'||!record(deps.foundation)||typeof deps.foundation.authorize!=='function'||typeof deps.foundation.canReadVerifiedScope!=='function'||!record(deps.store))throw new TypeError('ETP_E4_DEPENDENCIES_INVALID');
    var store=deps.store;
    ['getSourceIntake','getSourceBinding','saveSourceIntake','getVersions','saveVersion','findOperation'].forEach(function(name){if(typeof store[name]!=='function')throw new TypeError('ETP_E4_STORE_INVALID');});
    function context(request){var s=record(request)?scope(request.scopeKey):null,b=s&&binding(request.binding,s);return s&&b?{scope:s,binding:b}:null;}
    function restoredAllowed(request,c){return request.restored===undefined||deps.foundation.canReadVerifiedScope(request.restored,c.scope.scopeKey,c.binding)===true;}
    function authorize(request,action,c){return deps.foundation.authorize(request.activeAuthority,{domain:'E4',action:action,actorId:request.actorId,actorRole:request.actorRole,storeCode:c.scope.storeCode,at:request.at,reauthenticatedAt:request.reauthenticatedAt});}
    function readiness(request){
      var c=context(request);if(!c)return fail('E4_BINDING_INVALID');if(!restoredAllowed(request,c))return fail('E4_RESTORE_REIMPORT_REQUIRED');
      var intake=sources(store.getSourceIntake(c.scope.scopeKey),c.scope),storedBinding=store.getSourceBinding(c.scope.scopeKey);if(!intake)return fail('E4_APPROVED_SOURCES_REQUIRED');if(!storedBinding||storedBinding.source!=='ETP_VERIFIED'||storedBinding.scopeKey!==c.binding.scopeKey||storedBinding.generationId!==c.binding.generationId||storedBinding.receiptId!==c.binding.receiptId)return fail('E4_SOURCE_BINDING_MISMATCH');
      return freeze({ok:true,readiness:{contractVersion:VERSION,status:'READY',scope:c.scope,binding:c.binding,sourceHashes:intake.sourceHashes}});
    }
    function load(request){
      var ready=readiness(request),history;if(!ready.ok)return ready;history=store.getVersions(ready.readiness.scope.scopeKey);if(!Array.isArray(history))return fail('E4_VERSION_STORE_INVALID');
      return freeze({ok:true,model:{contractVersion:VERSION,status:'READY',scope:ready.readiness.scope,binding:ready.readiness.binding,sourceHashes:ready.readiness.sourceHashes,versions:history,activeVersion:history.length?history[history.length-1]:null}});
    }
    function intakeSources(request){
      var c=context(request);if(!c)return fail('E4_BINDING_INVALID');var grant=authorize(request,'PUBLISH',c);if(!grant.ok)return grant;
      var checked=sources(request.sources,c.scope);if(!checked)return fail('E4_APPROVED_SOURCES_REQUIRED');var prior=store.findOperation(c.scope.scopeKey,request.operationId);
      if(prior)return prior.kind==='SOURCE_INTAKE'&&prior.inputIdentity===JSON.stringify(checked.sourceHashes)?freeze({ok:true,idempotent:true,intake:prior.value}):fail('E4_IDEMPOTENCY_CONFLICT');
      var value=freeze({contractVersion:VERSION,kind:'SOURCE_INTAKE',scope:c.scope,binding:c.binding,sourceHashes:checked.sourceHashes,sources:checked.items,authorityGrantId:grant.grant.grantId,operationId:text(request.operationId,/^[A-Za-z0-9][A-Za-z0-9._:-]{2,95}$/)});
      if(!value.operationId)return fail('E4_OPERATION_ID_INVALID');store.saveSourceIntake(c.scope.scopeKey,value.sources,{kind:'SOURCE_INTAKE',inputIdentity:JSON.stringify(checked.sourceHashes),value:value,operationId:value.operationId});return freeze({ok:true,idempotent:false,intake:value});
    }
    function change(request,action){
      var c=context(request);if(!c)return fail('E4_BINDING_INVALID');var ready=readiness(request);if(!ready.ok)return ready;var grant=authorize(request,action,c);if(!grant.ok)return grant;
      var op=text(request.operationId,/^[A-Za-z0-9][A-Za-z0-9._:-]{2,95}$/);if(!op)return fail('E4_OPERATION_ID_INVALID');var identity=JSON.stringify({action:action,input:request.planInput,binding:c.binding,sourceHashes:ready.readiness.sourceHashes});var prior=store.findOperation(c.scope.scopeKey,op);
      if(prior)return prior.kind===action&&prior.inputIdentity===identity?freeze({ok:true,idempotent:true,published:prior.value}):fail('E4_IDEMPOTENCY_CONFLICT');
      if(!record(request.planInput)||request.planInput.storeCode!==c.scope.storeCode||request.planInput.periodStart!==c.scope.periodStart||request.planInput.periodEnd!==c.scope.periodEnd)return fail('E4_PLAN_SCOPE_MISMATCH');
      var history=store.getVersions(c.scope.scopeKey);if(!Array.isArray(history))return fail('E4_VERSION_STORE_INVALID');var result=deps.planning.publish(request.planInput,history);if(!result||result.ok!==true)return result&&result.code?result:fail('E4_PUBLICATION_FAILED');
      if(action==='PUBLISH'&&result.version.version!==1)return fail('E4_INITIAL_VERSION_REQUIRED');if(action!=='PUBLISH'&&result.version.version<=1)return fail('E4_REVISION_VERSION_REQUIRED');
      var persistedVersion=freeze(Object.assign({},result.version,{operationalBinding:c.binding,operationalSourceHashes:ready.readiness.sourceHashes}));var envelope=freeze({contractVersion:VERSION,action:action,scope:c.scope,binding:c.binding,sourceHashes:ready.readiness.sourceHashes,authorityGrantId:grant.grant.grantId,operationId:op,version:persistedVersion});
      store.saveVersion(c.scope.scopeKey,persistedVersion,{kind:action,inputIdentity:identity,value:envelope,operationId:op});return freeze({ok:true,idempotent:false,published:envelope});
    }
    function pace(request){
      var c=context(request);if(!c)return fail('E4_BINDING_INVALID');var ready=readiness(request);if(!ready.ok)return ready;if(!record(request.actuals)||request.actuals.generationId!==c.binding.generationId||request.actuals.receiptId!==c.binding.receiptId||request.actuals.source!=='ETP_VERIFIED')return fail('E4_ACTUAL_BINDING_MISMATCH');
      if(request.declarations!==undefined&&(!Array.isArray(request.declarations)||request.declarations.length))return fail('E4_DECLARATIONS_NOT_ACHIEVEMENT');var history=store.getVersions(c.scope.scopeKey);if(!Array.isArray(history)||!history.length)return fail('E4_VERSION_REQUIRED');var version=history[history.length-1],versionBinding=version.operationalBinding;if(!versionBinding||versionBinding.scopeKey!==c.binding.scopeKey||versionBinding.generationId!==c.binding.generationId||versionBinding.receiptId!==c.binding.receiptId)return fail('E4_VERSION_BINDING_MISMATCH');if(request.versionId!==version.versionId)return fail('E4_VERSION_BINDING_MISMATCH');
      var result=deps.planning.compute(version,{asOfDate:request.asOfDate,approvedLeave:request.approvedLeave,actuals:request.actuals,declarations:request.declarations});if(!result||result.ok!==true)return result&&result.code?result:fail('E4_PACE_FAILED');
      return freeze({ok:true,pace:{contractVersion:VERSION,scope:c.scope,binding:c.binding,versionId:version.versionId,sourceHashes:ready.readiness.sourceHashes,plan:result.plan}});
    }
    return freeze({VERSION:VERSION,readiness:readiness,load:load,intakeSources:intakeSources,publish:function(r){return change(r,'PUBLISH');},revise:function(r){return change(r,'REVISE');},reallocate:function(r){return change(r,'REALLOCATE');},pace:pace});
  }
  return freeze({VERSION:VERSION,SOURCE_TYPES:TYPES,create:create});
});
