/* Durable ETP control metadata. Facts and workbook material are prohibited. */
(function(root,factory){var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpControlRegistry=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';var KEY='saagar_etp_control_registry_v1',MAX_HISTORY=40;
  function create(options){var storage=options&&options.storage,core=options&&options.core;if(!storage||typeof storage.getItem!=='function'||typeof storage.setItem!=='function'||!core)return{ok:false,code:'ETP_CONTROL_DEPENDENCY_INVALID'};
    function load(){try{var value=JSON.parse(storage.getItem(KEY)||'{"scopes":{}}');return value&&value.scopes&&typeof value.scopes==='object'?value:{scopes:{}};}catch(_){return{scopes:{}};}}
    function get(scopeKey){var item=load().scopes[String(scopeKey||'')];if(!item||!item.current)return{ok:false,code:'ETP_RECEIPT_NOT_FOUND'};var checked=core.validateReceipt(item.current);return checked.ok?{ok:true,receipt:item.current}:checked;}
    function save(receipt){var checked=core.validateReceipt(receipt);if(!checked.ok)return checked;var db=load(),key=receipt.scopeKey,item=db.scopes[key]||{history:[]};item.history=Array.isArray(item.history)?item.history:[];if(item.current&&item.current.activeGenerationId!==receipt.activeGenerationId)item.history.unshift(item.current);item.history=item.history.slice(0,MAX_HISTORY);item.current=receipt;db.scopes[key]=item;try{var text=JSON.stringify(db);if(text.length>512*1024)return{ok:false,code:'ETP_CONTROL_CAPACITY_EXCEEDED'};storage.setItem(KEY,text);return{ok:true};}catch(_){return{ok:false,code:'ETP_CONTROL_WRITE_FAILED'};}}
    function acceptedLifecycle(scopeKey){var found=get(scopeKey);return found.ok?found.receipt.lifecycle:null;}
    return{ok:true,registry:Object.freeze({getReceipt:get,saveReceipt:save,acceptedLifecycle:acceptedLifecycle})};
  }
  return Object.freeze({VERSION:1,KEY:KEY,MAX_HISTORY:MAX_HISTORY,create:create});
});
