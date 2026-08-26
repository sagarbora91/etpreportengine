/* V6 ETP: iframe host adapter for governed E3/E4/E6/E5 operational presentations. */
(function(root,factory){'use strict';var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpOperationalModuleHost=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VERSION='ETP_OPERATIONAL_MODULE_HOST_V1';
  function freeze(v){if(v&&typeof v==='object'&&!Object.isFrozen(v)){Object.keys(v).forEach(function(k){freeze(v[k]);});Object.freeze(v);}return v;}
  function fail(code){return freeze({ok:false,code:code});}
  function record(v){return!!v&&typeof v==='object'&&!Array.isArray(v);}
  function clear(host){while(host&&host.firstChild)host.removeChild(host.firstChild);}
  function unavailable(host,message){if(!host||!host.ownerDocument)return;clear(host);var doc=host.ownerDocument,box=doc.createElement('section'),title=doc.createElement('h2'),body=doc.createElement('p');box.className='etp-operational-unavailable';box.setAttribute('role','alert');box.setAttribute('aria-live','polite');title.textContent='Operational ETP unavailable';body.textContent=message;box.appendChild(title);box.appendChild(body);host.appendChild(box);}
  function roots(doc){if(!doc||typeof doc.querySelector!=='function')return null;var area=doc.querySelector('[data-etp-phase6h-operations]'),e3=doc.querySelector('[data-etp-operational-e3]'),e4=doc.querySelector('[data-etp-operational-e4]'),e6=doc.querySelector('[data-etp-operational-e6]'),e5=doc.querySelector('[data-etp-operational-e5]');return area&&e3&&e4&&e6&&e5&&[e3,e4,e6,e5].every(function(node,index,list){return list.indexOf(node)===index;})?{area:area,e3:e3,e4:e4,e6:e6,e5:e5}:null;}
  function bridge(value){
    if(!record(value))return null;
    var operational=record(value.operational)?value.operational:value;
    var e3=operational.E3||operational.e3,e4=operational.E4||operational.e4,e6=operational.E6||operational.e6,e5=operational.E5||operational.e5;
    if(!record(e3)||!record(e4)||!record(e6)||!record(e5))return null;
    return {e3:e3,e4:e4,e6:e6,e5:e5,getVerifiedBinding:typeof value.getVerifiedBinding==='function'?value.getVerifiedBinding:undefined,getE4ActionInput:typeof value.getE4ActionInput==='function'?value.getE4ActionInput:undefined,getE6ActionInput:typeof value.getE6ActionInput==='function'?value.getE6ActionInput:undefined,getE5ActionInput:typeof value.getE5ActionInput==='function'?value.getE5ActionInput:undefined,subscribeScopeChange:typeof value.subscribeScopeChange==='function'?value.subscribeScopeChange:undefined};
  }
  function mount(options){
    var doc=record(options)&&options.document,found=roots(doc),facades=bridge(options&&options.bridge),mountApi=options&&options.mountApi,presentations=options&&options.presentations;
    var valid=found&&facades&&record(mountApi)&&typeof mountApi.mount==='function'&&record(presentations)&&record(presentations.e3)&&record(presentations.e4)&&record(presentations.e6)&&record(presentations.e5)&&typeof options.getScope==='function'&&typeof options.getActor==='function';
    if(!valid){if(found){unavailable(found.e3,'Governed E3 dependencies are unavailable.');unavailable(found.e4,'Governed E4 dependencies are unavailable.');unavailable(found.e6,'Governed E6 dependencies are unavailable.');unavailable(found.e5,'Governed E5 dependencies are unavailable.');}return fail('ETP_OPERATIONAL_HOST_DEPENDENCY_UNAVAILABLE');}
    var made;
    try{made=mountApi.mount({roots:{e3:found.e3,e4:found.e4,e6:found.e6,e5:found.e5},bridge:facades,presentations:presentations,getScope:options.getScope,getActor:options.getActor});}catch(_){unavailable(found.e3,'Governed E3 mounting failed closed.');unavailable(found.e4,'Governed E4 mounting failed closed.');unavailable(found.e6,'Governed E6 mounting failed closed.');unavailable(found.e5,'Governed E5 mounting failed closed.');return fail('ETP_OPERATIONAL_HOST_MOUNT_FAILED');}
    if(!made||made.ok!==true||!made.controller||typeof made.controller.refresh!=='function'||typeof made.controller.destroy!=='function')return fail('ETP_OPERATIONAL_HOST_MOUNT_FAILED');
    var destroyed=false;
    async function refresh(){if(destroyed)return fail('ETP_OPERATIONAL_HOST_DESTROYED');try{return await made.controller.refresh();}catch(_){unavailable(found.e3,'Governed E3 refresh failed closed.');unavailable(found.e4,'Governed E4 refresh failed closed.');unavailable(found.e6,'Governed E6 refresh failed closed.');unavailable(found.e5,'Governed E5 refresh failed closed.');return fail('ETP_OPERATIONAL_HOST_REFRESH_FAILED');}}
    function destroy(){if(destroyed)return freeze({ok:true,idempotent:true});destroyed=true;try{return made.controller.destroy();}catch(_){clear(found.e3);clear(found.e4);clear(found.e6);clear(found.e5);return fail('ETP_OPERATIONAL_HOST_DESTROY_FAILED');}}
    var controller=freeze({VERSION:VERSION,refresh:refresh,destroy:destroy});
    return freeze({ok:true,controller:controller,ready:Promise.resolve().then(refresh)});
  }
  async function compose(options){
    var doc=record(options)&&options.document,found=roots(doc),composer=options&&options.composer;
    if(!found||!record(composer)||typeof composer.compose!=='function'||typeof options.getScope!=='function'){if(found){unavailable(found.e3,'Governed E3 composition is unavailable.');unavailable(found.e4,'Governed E4 composition is unavailable.');unavailable(found.e6,'Governed E6 composition is unavailable.');unavailable(found.e5,'Governed E5 composition is unavailable.');}return fail('ETP_OPERATIONAL_HOST_COMPOSER_UNAVAILABLE');}
    var made;try{made=await composer.compose({roots:{e3:found.e3,e4:found.e4,e6:found.e6,e5:found.e5},getScope:options.getScope});}catch(_){made=null;}
    if(!made||made.ok!==true||!made.controller||typeof made.controller.refresh!=='function'||typeof made.controller.destroy!=='function'){unavailable(found.e3,'Governed E3 is blocked until an authorized verified WLMHW scope is selected.');unavailable(found.e4,'Governed E4 is blocked until its independent authority is approved.');unavailable(found.e6,'Governed E6 is blocked until its policy authority and verified binding are approved.');unavailable(found.e5,'Governed E5 is blocked until its incentive authority, verified period and controlled Payroll bridge are approved.');return fail(made&&made.code||'ETP_OPERATIONAL_HOST_COMPOSE_FAILED');}
    return freeze({ok:true,controller:made.controller,status:made.status});
  }
  return freeze({VERSION:VERSION,mount:mount,compose:compose});
});
