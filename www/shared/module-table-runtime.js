(function(root,factory){
  'use strict';
  var api=factory();
  if(typeof module==='object'&&module.exports){module.exports=api;return;}
  Object.defineProperty(root,'SaagarTableFoundation',{value:api,enumerable:true,writable:false,configurable:false});
})(typeof window!=='undefined'?window:globalThis,function(){
  'use strict';
  var STRATEGIES=Object.freeze(['cards','priority','grid']);
  var CLASSES=Object.freeze({cards:'saagar-table--cards',priority:'saagar-table--priority',grid:'saagar-table--grid'});
  var AUDIT_IDS=Object.freeze(['row-details-v1','row-action-v1']);
  var audited=[];
  function hasOptIn(element){
    var node=element;
    while(node){
      if(typeof node.hasAttribute==='function'&&node.hasAttribute('data-saagar-ui'))return true;
      node=node.parentNode;
    }
    return false;
  }
  function assertTable(table){
    if(!table||typeof table.setAttribute!=='function'||!table.classList||typeof table.classList.add!=='function')throw new TypeError('table must be an element');
    if(!hasOptIn(table))throw new Error('table requires a data-saagar-ui ancestor');
  }
  function strategyClasses(table){
    return STRATEGIES.filter(function(name){return table.classList.contains(CLASSES[name]);});
  }
  function applyStrategy(table,strategy,options){
    assertTable(table);
    var name=String(strategy||'');
    if(STRATEGIES.indexOf(name)<0)throw new TypeError('unsupported table strategy: '+name);
    var active=strategyClasses(table);
    if(active.length>1||(active.length===1&&active[0]!==name))throw new Error('table strategies are mutually exclusive');
    if(name==='grid'){
      var reason=options&&typeof options.reason==='string'?options.reason.trim():'';
      if(!reason)throw new Error('grid strategy requires a documented reason');
      table.setAttribute('data-saagar-grid-reason',reason);
    }
    table.classList.add('saagar-table');
    table.classList.add(CLASSES[name]);
    return table;
  }
  function createTextCell(document,label,value){
    if(!document||typeof document.createElement!=='function')throw new TypeError('document is required');
    var cell=document.createElement('td');
    cell.setAttribute('data-saagar-label',String(label===undefined?'':label));
    cell.textContent=String(value===undefined||value===null?'':value);
    return cell;
  }
  function canonicalId(value,label){
    var id=String(value||'');
    if(!/^[a-z][a-z0-9-]{0,63}$/.test(id))throw new TypeError(label+' must be a canonical identifier');
    return id;
  }
  function createAuditedControl(document,auditId,model){
    if(!document||typeof document.createElement!=='function')throw new TypeError('document is required');
    var id=String(auditId||'');
    if(AUDIT_IDS.indexOf(id)<0)throw new TypeError('unknown audited control contract');
    if(!model||Object.prototype.toString.call(model)!=='[object Object]')throw new TypeError('control model must be a plain object');
    var element=document.createElement('button');
    element.setAttribute('type','button');
    element.textContent=String(model.label===undefined?'':model.label);
    if(id==='row-details-v1'){
      element.setAttribute('data-saagar-row-details','');
      element.setAttribute('aria-expanded','false');
      element.setAttribute('aria-controls',canonicalId(model.controlsId,'controlsId'));
    }else{
      element.setAttribute('data-action',canonicalId(model.actionId,'actionId'));
      element.setAttribute('data-saagar-row-action','');
    }
    var token=Object.freeze({auditId:id});
    audited.push({token:token,element:element});
    return token;
  }
  function createControlCell(document,label,controlToken){
    if(!document||typeof document.createElement!=='function')throw new TypeError('document is required');
    var record=null;
    for(var i=0;i<audited.length;i++)if(audited[i].token===controlToken){record=audited[i];break;}
    if(!record)throw new TypeError('control requires an opaque audited token');
    var cell=document.createElement('td');
    cell.setAttribute('data-saagar-label',String(label===undefined?'':label));
    cell.setAttribute('data-saagar-audited-control',record.token.auditId);
    cell.appendChild(record.element);
    return cell;
  }
  function setPriority(cell,value){
    var priority=Number(value);
    if(priority!==1&&priority!==2&&priority!==3&&priority!==4)throw new TypeError('priority must be 1, 2, 3 or 4');
    if(!cell||typeof cell.setAttribute!=='function')throw new TypeError('cell must be an element');
    cell.setAttribute('data-saagar-priority',String(priority));
    return cell;
  }
  return Object.freeze({version:1,strategies:STRATEGIES,auditIds:AUDIT_IDS,applyStrategy:applyStrategy,createTextCell:createTextCell,createAuditedControl:createAuditedControl,createControlCell:createControlCell,setPriority:setPriority});
});
