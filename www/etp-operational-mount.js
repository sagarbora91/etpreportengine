/* V6 ETP: bounded iframe-side composition for governed E3, E4, E6 and E5 presentations. */
(function(root,factory){'use strict';var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpOperationalMount=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VERSION='ETP_OPERATIONAL_MOUNT_V1';
  function freeze(v){if(v&&typeof v==='object'&&!Object.isFrozen(v)){Object.keys(v).forEach(function(k){freeze(v[k]);});Object.freeze(v);}return v;}
  function fail(code){return freeze({ok:false,code:code});}
  function record(v){return !!v&&typeof v==='object'&&!Array.isArray(v);}
  function clear(host){while(host&&host.firstChild)host.removeChild(host.firstChild);}
  function unavailable(host,message){if(!host||!host.ownerDocument)return;clear(host);var box=host.ownerDocument.createElement('section'),title=host.ownerDocument.createElement('h2'),body=host.ownerDocument.createElement('p');box.className='etp-operational-unavailable';box.setAttribute('role','alert');box.setAttribute('aria-live','polite');title.textContent='Operational ETP unavailable';body.textContent=message;box.appendChild(title);box.appendChild(body);host.appendChild(box);}
  function copy(provider){var value;try{value=provider();}catch(_){return null;}if(!record(value))return null;return JSON.parse(JSON.stringify(value));}
  function mount(options){
    var roots=record(options)&&options.roots,bridge=record(options)&&options.bridge,presentations=record(options)&&options.presentations;
    var valid=record(roots)&&roots.e3&&roots.e4&&roots.e6&&roots.e5&&record(bridge)&&record(bridge.e3)&&record(bridge.e4)&&record(bridge.e6)&&record(bridge.e5)&&record(presentations)&&record(presentations.e3)&&typeof presentations.e3.create==='function'&&record(presentations.e4)&&typeof presentations.e4.mount==='function'&&record(presentations.e6)&&typeof presentations.e6.mount==='function'&&record(presentations.e5)&&typeof presentations.e5.mount==='function'&&typeof options.getScope==='function'&&typeof options.getActor==='function';
    if(!valid){if(roots){unavailable(roots.e3,'Governed E3 bridge or presentation dependency is unavailable.');unavailable(roots.e4,'Governed E4 bridge or presentation dependency is unavailable.');unavailable(roots.e6,'Governed E6 bridge or presentation dependency is unavailable.');unavailable(roots.e5,'Governed E5 bridge or presentation dependency is unavailable.');}return fail('ETP_OPERATIONAL_DEPENDENCY_UNAVAILABLE');}
    var destroyed=false,busy=false,queued=false,unsubscribe=null,e3Result,e4Result,e6Result,e5Result,activeScope=null,activeActor=null;
    function liveScope(){return copy(options.getScope);}
    function liveActor(){return copy(options.getActor);}
    function scope(){return activeScope?JSON.parse(JSON.stringify(activeScope)):null;}
    function e3Actor(){var actor=activeActor,role=actor&&(actor.actorRole||actor.role);return actor&&role?{actorId:actor.actorId,actorRole:role,at:actor.at,reauthenticatedAt:actor.reauthenticatedAt}:null;}
    function e4Actor(){var actor=activeActor,role=actor&&(actor.role||actor.actorRole);return actor&&role?{actorId:actor.actorId,role:role,at:actor.at,reauthenticatedAt:actor.reauthenticatedAt}:null;}
    activeScope=liveScope();activeActor=liveActor();
    e3Result=presentations.e3.create({host:roots.e3,facade:bridge.e3,getScope:scope,getActor:e3Actor,getVerifiedBinding:typeof bridge.getVerifiedBinding==='function'?function(){return bridge.getVerifiedBinding(scope());}:undefined});
    e4Result=presentations.e4.mount(roots.e4,bridge.e4,{getScope:scope,getActor:e4Actor,getActionInput:typeof bridge.getE4ActionInput==='function'?function(name){return bridge.getE4ActionInput(name,scope());}:undefined});
    e6Result=presentations.e6.mount(roots.e6,bridge.e6,{getScope:scope,getActor:e4Actor,getActionInput:typeof bridge.getE6ActionInput==='function'?function(name,id){return bridge.getE6ActionInput(name,id,scope());}:undefined});
    e5Result=presentations.e5.mount(roots.e5,bridge.e5,{getScope:scope,getActor:e4Actor,getActionInput:typeof bridge.getE5ActionInput==='function'?function(name){return bridge.getE5ActionInput(name,scope());}:undefined});
    if(!e3Result||e3Result.ok!==true||!e3Result.controller||typeof e3Result.controller.refresh!=='function'||!e4Result||e4Result.ok!==true||typeof e4Result.refresh!=='function'||!e6Result||e6Result.ok!==true||typeof e6Result.refresh!=='function'||!e5Result||e5Result.ok!==true||typeof e5Result.refresh!=='function'){
      if(e3Result&&e3Result.controller&&typeof e3Result.controller.destroy==='function')e3Result.controller.destroy();unavailable(roots.e3,'E3 governed presentation failed closed.');unavailable(roots.e4,'E4 governed presentation failed closed.');unavailable(roots.e6,'E6 governed presentation failed closed.');unavailable(roots.e5,'E5 governed presentation failed closed.');return fail('ETP_OPERATIONAL_MOUNT_FAILED');
    }
    async function refresh(){
      if(destroyed)return fail('ETP_OPERATIONAL_DESTROYED');if(busy){queued=true;return fail('ETP_OPERATIONAL_BUSY');}activeScope=liveScope();activeActor=liveActor();if(!scope()||!e3Actor()||!e4Actor()){unavailable(roots.e3,'Authorized store, period and actor context are required.');unavailable(roots.e4,'Authorized store, period and actor context are required.');unavailable(roots.e6,'Authorized store, period and actor context are required.');unavailable(roots.e5,'Authorized store, period and actor context are required.');return fail('ETP_OPERATIONAL_CONTEXT_REQUIRED');}
      busy=true;var results=await Promise.all([Promise.resolve().then(function(){return e3Result.controller.refresh();}).catch(function(){return fail('E3_REFRESH_FAILED');}),Promise.resolve().then(function(){return e4Result.refresh();}).catch(function(){return fail('E4_REFRESH_FAILED');}),Promise.resolve().then(function(){return e6Result.refresh();}).catch(function(){return fail('E6_REFRESH_FAILED');}),Promise.resolve().then(function(){return e5Result.refresh();}).catch(function(){return fail('E5_REFRESH_FAILED');})]);busy=false;
      if(queued&&!destroyed){queued=false;await refresh();}
      return freeze({ok:results.every(function(result){return result&&result.ok===true;}),results:freeze(results)});
    }
    function onScopeChange(){if(!destroyed)refresh();}
    if(typeof bridge.subscribeScopeChange==='function'){try{unsubscribe=bridge.subscribeScopeChange(onScopeChange);}catch(_){destroyed=true;e3Result.controller.destroy();clear(roots.e4);clear(roots.e6);clear(roots.e5);unavailable(roots.e3,'Scope coordination is unavailable.');unavailable(roots.e4,'Scope coordination is unavailable.');unavailable(roots.e6,'Scope coordination is unavailable.');unavailable(roots.e5,'Scope coordination is unavailable.');return fail('ETP_OPERATIONAL_SCOPE_SUBSCRIPTION_FAILED');}if(typeof unsubscribe!=='function'){destroyed=true;e3Result.controller.destroy();clear(roots.e4);clear(roots.e6);clear(roots.e5);unavailable(roots.e3,'Scope coordination is unavailable.');unavailable(roots.e4,'Scope coordination is unavailable.');unavailable(roots.e6,'Scope coordination is unavailable.');unavailable(roots.e5,'Scope coordination is unavailable.');return fail('ETP_OPERATIONAL_SCOPE_SUBSCRIPTION_FAILED');}}
    function destroy(){if(destroyed)return freeze({ok:true,idempotent:true});destroyed=true;queued=false;if(unsubscribe){try{unsubscribe();}catch(_){}}e3Result.controller.destroy();clear(roots.e4);clear(roots.e6);clear(roots.e5);return freeze({ok:true,idempotent:false});}
    return freeze({ok:true,controller:freeze({VERSION:VERSION,refresh:refresh,destroy:destroy})});
  }
  return freeze({VERSION:VERSION,mount:mount});
});
