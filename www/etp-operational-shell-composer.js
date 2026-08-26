/* V6 ETP: production shell composition for governed E3/E4/E6/E5 operations. */
(function(root,factory){'use strict';var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;else if(root)root.SaagarEtpOperationalShellComposer=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VERSION='ETP_OPERATIONAL_SHELL_COMPOSER_V1';
  var E3_AUTHORITY={domain:'E3',status:'ACTIVE',sourceSha256:'e111e348b2d9795ca818707185187f7bd7fbc10ace85e3c229d8e38ee2c2d241',approvalId:'E3-OWNER-2026-08-25-V1',approvedAt:'2026-08-25T00:00:00.000Z',approvedByRole:'Owner',stores:['WLMHW']};
  function freeze(v){if(v&&typeof v==='object'&&!Object.isFrozen(v)){Object.keys(v).forEach(function(k){freeze(v[k]);});Object.freeze(v);}return v;}
  function fail(code){return freeze({ok:false,code:code});}
  function rec(v){return!!v&&typeof v==='object'&&!Array.isArray(v);}
  function iso(v){return typeof v==='string'&&!isNaN(Date.parse(v));}
  function cleanScope(v){if(!rec(v)||v.storeCode!=='WLMHW'||!/^[0-9]{4}-[0-9]{2}$/.test(v.financialYear||'')||!/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(v.periodStart||'')||!/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(v.periodEnd||'')||v.periodStart>v.periodEnd)return null;var key=v.storeCode+'|'+v.financialYear+'|'+v.periodStart+'..'+v.periodEnd;if(v.scopeKey!==undefined&&v.scopeKey!==key)return null;return freeze({storeCode:v.storeCode,financialYear:v.financialYear,periodStart:v.periodStart,periodEnd:v.periodEnd,scopeKey:key,businessDate:typeof v.businessDate==='string'?v.businessDate:v.periodEnd});}
  function dependencies(r){return rec(r)&&r.SaagarEtpModuleGateway&&r.SaagarEtpModuleGateway.readFacade&&r.SaagarEtpOperationalBootstrap&&r.SaagarEtpOperationalMount&&r.SaagarEtpE3VerifiedJoin&&r.SaagarEtpOperationalFoundation&&r.SaagarEtpOperationalStore&&r.SaagarEtpOperationalRuntime&&r.SaagarEtpOperationalAdapters&&r.SaagarEtpCroReconciliation&&r.SaagarEtpTargetPlanning&&r.SaagarEtpExceptionMonitor&&r.SaagarEtpIncentiveControl&&r.SaagarEtpE5PayrollBridge&&r.SaagarEtpE3Orchestrator&&r.SaagarEtpE4Orchestrator&&r.SaagarEtpOperationalGateway&&r.SaagarEtpE3Presentation&&r.SaagarEtpE4Presentation&&r.SaagarEtpE6Presentation&&r.SaagarEtpE5Presentation&&r.SaagarOwnerSession&&typeof r.SaagarOwnerSession.read==='function'&&typeof r.SaagarReauth==='function'&&r.localStorage?r:null;}
  function actor(r,scope){var snapshot;try{snapshot=r.SaagarOwnerSession.read();}catch(_){return null;}if(!rec(snapshot)||snapshot.version!==1||snapshot.isOwner!==true)return null;return freeze({actorId:'OWNER_SESSION',role:'Owner',actorRole:'Owner',storeCode:scope.storeCode});}
  function binding(inspected,scope){var receipt=inspected&&inspected.currentReceipt,generation=receipt&&receipt.activeGenerationId;if(!inspected||inspected.ok!==true||!receipt||receipt.scopeKey!==scope.scopeKey||receipt.storeCode!=='WLMHW'||receipt.reconciliationStatus!=='PASS'||!/^etp_[a-f0-9]{32}$/.test(generation||''))return null;return freeze({scopeKey:scope.scopeKey,generationId:generation,receiptId:'receipt-'+generation.slice(4)});}
  async function compose(options){
    var suppliedRoot=rec(options)&&options.root,demo=suppliedRoot&&suppliedRoot.SaagarEtpDemoOperational;
    if(demo&&demo.syntheticOnly===true&&typeof demo.composeRetail==='function')return demo.composeRetail({root:suppliedRoot,roots:options.roots,getScope:options.getScope});
    var r=dependencies(suppliedRoot),scope=r&&typeof options.getScope==='function'?cleanScope(options.getScope()):null,roots=options&&options.roots;
    if(!r)return fail('ETP_SHELL_DEPENDENCY_UNAVAILABLE');
    if(!scope||!rec(roots)||!roots.e3||!roots.e4||!roots.e6||!roots.e5)return fail('ETP_SHELL_SCOPE_UNAVAILABLE');
    if(!actor(r,scope))return fail('ETP_SHELL_OWNER_SESSION_REQUIRED');
    var facade=r.SaagarEtpModuleGateway.readFacade,inspected;
    try{inspected=await facade.inspectScope(scope,{historyLimit:0});}catch(_){return fail('ETP_SHELL_VERIFIED_SCOPE_UNAVAILABLE');}
    var b=binding(inspected,scope),joined=b&&r.SaagarEtpE3VerifiedJoin.create({readFacade:facade,scope:{storeCode:scope.storeCode,financialYear:scope.financialYear,periodStart:scope.periodStart,periodEnd:scope.periodEnd},binding:b});
    if(!b||!joined||joined.ok!==true||!joined.reader)return fail('ETP_SHELL_VERIFIED_SCOPE_UNAVAILABLE');
    var verified=freeze({readE3:function(q){if(!rec(q)||q.scopeKey!==b.scopeKey||q.generationId!==b.generationId||q.receiptId!==b.receiptId)return Promise.resolve(fail('E3_VERIFIED_BINDING_INVALID'));return joined.reader.loadDay(q.businessDate);},getBinding:function(key){return key===b.scopeKey?b:null;},getNextBinding:function(){return null;}});
    var pendingGrant=null;
    function clock(){return new Date().toISOString();}
    function session(){return actor(r,scope);}
    function reauth(action){var grant=pendingGrant;pendingGrant=null;return grant&&grant.action===action?grant.at:null;}
    var boot;try{boot=r.SaagarEtpOperationalBootstrap.create({foundation:r.SaagarEtpOperationalFoundation,store:r.SaagarEtpOperationalStore,runtime:r.SaagarEtpOperationalRuntime,adapters:r.SaagarEtpOperationalAdapters,e3Engine:r.SaagarEtpCroReconciliation,e4Engine:r.SaagarEtpTargetPlanning,e6Engine:r.SaagarEtpExceptionMonitor,e5Engine:r.SaagarEtpIncentiveControl,e5PayrollBridge:r.SaagarEtpE5PayrollBridge,e3Orchestrator:r.SaagarEtpE3Orchestrator,e4Orchestrator:r.SaagarEtpE4Orchestrator,gateway:r.SaagarEtpOperationalGateway,verifiedJoin:verified,storage:r.localStorage,ownerSession:session,reauth:reauth,clock:clock,authorities:{E3:E3_AUTHORITY}});}catch(_){return fail('ETP_SHELL_BOOTSTRAP_FAILED');}
    if(!boot||boot.ok!==true||!boot.operational)return fail('ETP_SHELL_BOOTSTRAP_FAILED');
    async function privileged(name,action,args){var approved=false;try{approved=(await r.SaagarReauth('ETP '+action))===true;}catch(_){approved=false;}if(!approved)return fail('ETP_OPERATIONAL_CONTEXT_UNAVAILABLE');pendingGrant={action:action,at:clock()};try{return await boot.operational.E3[name].apply(boot.operational.E3,args);}catch(_){return fail('E3_OPERATION_FAILED');}finally{pendingGrant=null;}}
    var e3=freeze({load:function(){return boot.operational.E3.load.apply(boot.operational.E3,arguments);},open:function(){return boot.operational.E3.open.apply(boot.operational.E3,arguments);},declare:function(){return boot.operational.E3.declare.apply(boot.operational.E3,arguments);},close:function(){return privileged('close','CLOSE',arguments);},importVerified:function(){return privileged('importVerified','IMPORT',arguments);},reconcile:function(){return privileged('reconcile','RECONCILE',arguments);},markReconciled:function(){return privileged('markReconciled','DISPOSE',arguments);},correct:function(){return privileged('correct','CORRECT',arguments);},dispose:function(){return privileged('dispose','DISPOSE',arguments);},lock:function(){return privileged('lock','LOCK',arguments);},startRestatementCycle:function(){return privileged('startRestatementCycle','CORRECT_AFTER_FREEZE',arguments);}});
    var mounted;try{mounted=r.SaagarEtpOperationalMount.mount({roots:roots,bridge:{e3:e3,e4:boot.operational.E4,e6:boot.operational.E6,e5:boot.operational.E5,getVerifiedBinding:function(){return b;}},presentations:{e3:r.SaagarEtpE3Presentation,e4:r.SaagarEtpE4Presentation,e6:r.SaagarEtpE6Presentation,e5:r.SaagarEtpE5Presentation},getScope:function(){return scope;},getActor:function(){return session();}});}catch(_){return fail('ETP_SHELL_MOUNT_FAILED');}
    if(!mounted||mounted.ok!==true||!mounted.controller)return fail('ETP_SHELL_MOUNT_FAILED');
    var refreshed;try{refreshed=await mounted.controller.refresh();}catch(_){refreshed=null;}if(!refreshed||refreshed.ok!==true){try{mounted.controller.destroy();}catch(_){}return fail('ETP_SHELL_INITIAL_REFRESH_FAILED');}
    return freeze({ok:true,controller:mounted.controller,status:boot.operational.status});
  }
  return freeze({VERSION:VERSION,GATE0_E3_AUTHORITY:freeze(E3_AUTHORITY),compose:compose});
});
