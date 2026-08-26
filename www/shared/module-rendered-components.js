(function(root,factory){
  'use strict';
  var api=factory(root);
  if(typeof module==='object'&&module.exports){module.exports=api;return;}
  Object.defineProperty(root,'SaagarRenderedComponents',{value:api,enumerable:true,writable:false,configurable:false});
})(typeof window!=='undefined'?window:globalThis,function(root){
  'use strict';
  var ACTION=/^[a-z][a-z0-9-]{0,63}$/;
  var STRATEGY=/^(cards|priority|grid)$/;
  var verified=typeof WeakMap==='function'?new WeakMap():null;
  function fingerprint(control){return [control.getAttribute('data-action')||'',control.getAttribute('data-saagar-args')||'',control.getAttribute('onclick')||''].join('\n');}
  function plainRecord(value,label){
    if(!value||Object.getPrototypeOf(value)!==Object.prototype)throw new TypeError(label+' must be a plain object');
  }
  function createPolicy(actions,workflows){
    plainRecord(actions,'actions');plainRecord(workflows,'workflows');
    var safeActions=Object.create(null),safeWorkflows=Object.create(null);
    Object.keys(actions).forEach(function(action){
      if(!ACTION.test(action)||action==='__proto__'||action==='constructor'||action==='prototype')throw new TypeError('invalid policy action');
      var specification=actions[action];
      if(specification===null){safeActions[action]=null;return;}
      if(!specification||Object.getPrototypeOf(specification)!==Object.prototype)throw new TypeError('handler contract must be a plain object');
      if(!/^[A-Za-z_$][A-Za-z0-9_$]*$/.test(specification.handler)||[0,1,2].indexOf(specification.args)<0)throw new TypeError('invalid policy handler contract');
      safeActions[action]=Object.freeze({handler:specification.handler,args:specification.args,delegated:specification.delegated===true});
    });
    Object.keys(workflows).forEach(function(workflow){
      if(!ACTION.test(workflow)||workflow==='__proto__'||workflow==='constructor'||workflow==='prototype')throw new TypeError('invalid policy workflow');
      var model=workflows[workflow];plainRecord(model,'workflow');
      if(!STRATEGY.test(model.strategy))throw new TypeError('invalid workflow strategy');
      var reason=model.reason===undefined?'':String(model.reason);
      if(model.strategy==='grid'&&!reason)throw new TypeError('grid workflow requires reason');
      safeWorkflows[workflow]=Object.freeze({strategy:model.strategy,reason:reason});
    });
    return Object.freeze({actions:Object.freeze(safeActions),workflows:Object.freeze(safeWorkflows)});
  }
  function escapeText(value){
    return String(value===undefined||value===null?'':value).replace(/[&<>"']/g,function(character){
      return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[character];
    });
  }
  function encodeInlineString(value){
    if(value!==undefined&&value!==null&&typeof value!=='string'&&typeof value!=='number'&&typeof value!=='boolean')throw new TypeError('inline value must be primitive');
    var text=String(value===undefined||value===null?'':value);
    if(text.length>512)throw new RangeError('inline string exceeds 512 characters');
    return text.replace(/[\\'"&<>\u0000-\u001f\u007f\u2028\u2029]/g,function(character){
      var code=character.charCodeAt(0);
      var hex=code.toString(16);while(hex.length<(code<=255?2:4))hex='0'+hex;
      return code<=255?'\\x'+hex:'\\u'+hex;
    });
  }
  function encodeArgs(values){
    if(!Array.isArray(values)||values.length>2)throw new TypeError('arguments must be a bounded array');
    var safe=[];
    for(var i=0;i<values.length;i++){
      var value=values[i];
      if(value!==null&&typeof value!=='string'&&typeof value!=='number'&&typeof value!=='boolean')throw new TypeError('arguments must be primitive');
      if(typeof value==='string'&&value.length>512)throw new RangeError('argument exceeds 512 characters');
      safe.push(value);
    }
    return encodeURIComponent(JSON.stringify(safe));
  }
  function decodeArgs(value,count){
    var parsed=JSON.parse(decodeURIComponent(String(value||'')));
    if(!Array.isArray(parsed)||parsed.length!==count)throw new TypeError('argument schema mismatch');
    for(var i=0;i<parsed.length;i++)if(parsed[i]!==null&&typeof parsed[i]!=='string'&&typeof parsed[i]!=='number'&&typeof parsed[i]!=='boolean')throw new TypeError('argument schema mismatch');
    return parsed;
  }
  function enhance(scope,policy){
    if(!scope||typeof scope.querySelectorAll!=='function')throw new TypeError('scope must support querySelectorAll');
    if(!policy||!Object.isFrozen(policy)||!Object.isFrozen(policy.actions)||!Object.isFrozen(policy.workflows))throw new TypeError('frozen rendered policy is required');
    var foundation=root&&root.SaagarTableFoundation;
    if(!foundation||!Object.isFrozen(foundation)||!Object.prototype.hasOwnProperty.call(foundation,'applyStrategy')||typeof foundation.applyStrategy!=='function')throw new Error('frozen SaagarTableFoundation is required');
    var tables=scope.querySelectorAll('table[data-saagar-table-strategy]');
    var rejected=0;
    for(var i=0;i<tables.length;i++){
      var table=tables[i],strategy=table.getAttribute('data-saagar-table-strategy');
      try{
        var workflow=table.getAttribute('data-saagar-table-workflow'),tablePolicy=Object.prototype.hasOwnProperty.call(policy.workflows,workflow)&&policy.workflows[workflow];
        if(!tablePolicy||strategy!==tablePolicy.strategy)throw new TypeError('unapproved rendered table workflow');
        if(strategy==='grid'&&table.getAttribute('data-saagar-grid-reason')!==tablePolicy.reason)throw new TypeError('unapproved rendered grid reason');
        foundation.applyStrategy(table,strategy,strategy==='grid'?{reason:tablePolicy.reason}:undefined);
        if(verified)verified.set(table,[workflow,strategy,table.getAttribute('data-saagar-grid-reason')||''].join('\n'));
      }catch(error){rejected++;table.setAttribute('data-saagar-render-rejected','table');}
    }
    var controls=scope.querySelectorAll('button[data-action]');
    for(var j=0;j<controls.length;j++){
      var control=controls[j];
      try{
        if(control.getAttribute('data-saagar-audited-control')&&(!verified||!verified.has(control)))throw new TypeError('pre-forged audit attribute');
        var action=control.getAttribute('data-action')||'';
        if(!ACTION.test(action)||!Object.prototype.hasOwnProperty.call(policy.actions,action))throw new TypeError('unapproved rendered action');
        var expected=policy.actions[action],source=control.getAttribute('onclick');
        if(expected===null){if(source)throw new TypeError('unexpected inline handler');}
        else if(expected.delegated){if(source)throw new TypeError('delegated control cannot carry inline handler');decodeArgs(control.getAttribute('data-saagar-args'),expected.args);}
        else{
          var argument="(?:this(?:\\.value)?|'(?:\\\\x[0-9a-fA-F]{2}|\\\\u[0-9a-fA-F]{4}|[^'\\\\])*'|[0-9]+)";
          var argumentsPattern=expected.args===0?'':expected.args===1?argument:argument+'\\s*,\\s*'+argument;
          var exact=new RegExp('^\\s*'+expected.handler.replace(/[$]/g,'\\$&')+'\\s*\\(\\s*'+argumentsPattern+'\\s*\\)\\s*;?\\s*$');
          if(!source||!exact.test(source))throw new TypeError('handler provenance mismatch');
        }
        if(!control.getAttribute('type'))control.setAttribute('type','button');
        control.setAttribute('data-saagar-audited-control','row-action-v1');
        if(verified)verified.set(control,fingerprint(control));
      }catch(error){rejected++;control.setAttribute('data-saagar-render-rejected','control');control.setAttribute('disabled','disabled');}
    }
    return Object.freeze({tables:tables.length,controls:controls.length,rejected:rejected});
  }
  function observe(scope,policy){
    enhance(scope,policy);
    if(!root||typeof root.MutationObserver!=='function')return Object.freeze({disconnect:function(){}});
    var observer=new root.MutationObserver(function(records){
      for(var i=0;i<records.length;i++)for(var j=0;j<records[i].addedNodes.length;j++){
        var node=records[i].addedNodes[j];
        if(node&&node.nodeType===1)enhance(node.matches&&node.matches('table[data-saagar-table-strategy],button[data-action]')?node.parentNode:node,policy);
      }
      for(var k=0;k<records.length;k++)if(records[k].type==='attributes'){
        var target=records[k].target;
        if(records[k].oldValue===target.getAttribute(records[k].attributeName))continue;
        if(verified)verified.delete(target);enhance(target.parentNode||scope,policy);
      }
    });
    observer.observe(scope,{childList:true,subtree:true,attributes:true,attributeOldValue:true,attributeFilter:['data-action','data-saagar-args','onclick','data-saagar-table-strategy','data-saagar-table-workflow','data-saagar-grid-reason']});
    return Object.freeze({disconnect:function(){observer.disconnect();}});
  }
  function connect(scope,policy,handlers){
    if(!scope||typeof scope.addEventListener!=='function'||typeof scope.removeEventListener!=='function')throw new TypeError('scope must be an event target');
    plainRecord(handlers,'handlers');
    Object.keys(policy.actions).forEach(function(action){var model=policy.actions[action];if(model&&model.delegated){if(!Object.prototype.hasOwnProperty.call(handlers,model.handler)||typeof handlers[model.handler]!=='function')throw new TypeError('missing frozen handler');}});
    var copy=Object.create(null);Object.keys(handlers).forEach(function(name){copy[name]=handlers[name];});var frozen=Object.freeze(copy),active=true;
    function dispatch(event){
      var control=event.target&&typeof event.target.closest==='function'?event.target.closest('button[data-action]'):null;
      if(!control)return;
      var action=control.getAttribute('data-action'),model=Object.prototype.hasOwnProperty.call(policy.actions,action)&&policy.actions[action];
      if(!model||!model.delegated)return;
      try{
        if(typeof scope.contains!=='function'||!scope.contains(control))throw new TypeError('control is outside delegated scope');
        if(!verified||!verified.has(control)||verified.get(control)!==fingerprint(control))throw new TypeError('control is not currently verified');
        if(control.getAttribute('data-saagar-audited-control')!=='row-action-v1')throw new TypeError('control audit marker mismatch');
        if(control.getAttribute('disabled')!==null||control.getAttribute('data-saagar-render-rejected')!==null)throw new TypeError('control is disabled or rejected');
        if(control.getAttribute('onclick'))throw new TypeError('delegated control cannot carry inline handler');
        var args=decodeArgs(control.getAttribute('data-saagar-args'),model.args);
        frozen[model.handler].apply(null,args);
      }catch(error){control.setAttribute('disabled','disabled');control.setAttribute('data-saagar-render-rejected','dispatch');event.preventDefault();if(typeof event.stopImmediatePropagation==='function')event.stopImmediatePropagation();}
    }
    scope.addEventListener('click',dispatch,true);
    return Object.freeze({disconnect:function(){if(!active)return;active=false;scope.removeEventListener('click',dispatch,true);}});
  }
  return Object.freeze({version:1,escapeText:escapeText,encodeInlineString:encodeInlineString,encodeArgs:encodeArgs,decodeArgs:decodeArgs,createPolicy:createPolicy,enhance:enhance,observe:observe,connect:connect});
});
