/* Phase 6H.1: read-only consumers for the parent-owned ETP E2 analytics facade.
   This runtime receives only the frozen sanitized analytics model. */
(function(root,factory){var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)Object.defineProperty(root,'SaagarEtpAnalyticsConsumer',{value:api,enumerable:true,writable:false,configurable:false});})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VIEWS=Object.freeze(['DAY','MTD','YTD','LY']);
  function exactDate(value){return /^\d{4}-\d{2}-\d{2}$/.test(String(value||''));}
  function store(value){value=String(value||'').toUpperCase();return /^(?:WLMHW|HEMW)$/.test(value)?value:null;}
  function failure(code){return Object.freeze({ok:false,code:code});}
  function contains(scope,date){return scope&&scope.periodStart<=date&&scope.periodEnd>=date;}
  function money(value){return value===null?'—':'₹'+(Number(value)/100).toLocaleString('en-IN',{minimumFractionDigits:2,maximumFractionDigits:2});}
  function number(value,scale){return value===null?'—':(Number(value)/(scale||1)).toLocaleString('en-IN',{maximumFractionDigits:2});}
  function text(doc,tag,value,className){var node=doc.createElement(tag);if(className)node.className=className;node.textContent=String(value==null?'':value);return node;}
  function clear(node){while(node&&node.firstChild)node.removeChild(node.firstChild);}
  function validate(value,expectedStore){
    if(!value||value.contractVersion!=='ETP_E2_ANALYTICS_V1'||!value.scope||value.scope.storeCode!==expectedStore||VIEWS.indexOf(value.view)<0||!value.period||!value.verified||!value.coverage||!value.metrics||!value.mixes||!value.exceptions||!value.identity)return false;
    return value.identity.reconciles===true&&value.identity.storeNet===value.identity.croAchievement+value.identity.unassigned;
  }
  async function load(facade,storeCode,asOfDate,view){
    var selected=store(storeCode),date=String(asOfDate||''),mode=String(view||'DAY').toUpperCase();
    if(!selected)return failure('ETP_ANALYTICS_STORE_REQUIRED');
    if(!exactDate(date)||VIEWS.indexOf(mode)<0)return failure('ETP_ANALYTICS_CONSUMER_REQUEST_INVALID');
    if(!facade||typeof facade.listScopes!=='function'||typeof facade.loadAnalytics!=='function')return failure('ETP_ANALYTICS_UNAVAILABLE');
    var listed;
    try{listed=await facade.listScopes({limit:20});}catch(_){return failure('ETP_ANALYTICS_SCOPE_FAILED');}
    if(!listed||listed.ok!==true||!Array.isArray(listed.scopes))return failure('ETP_ANALYTICS_SCOPE_FAILED');
    var matches=listed.scopes.map(function(item){return item&&item.scope;}).filter(function(scope){return scope&&scope.storeCode===selected&&contains(scope,date);});
    if(matches.length!==1)return failure(matches.length?'ETP_ANALYTICS_SCOPE_AMBIGUOUS':'ETP_ANALYTICS_SCOPE_MISSING');
    var result;
    try{result=await facade.loadAnalytics(matches[0],{view:mode,asOfDate:date});}catch(_){return failure('ETP_ANALYTICS_LOAD_FAILED');}
    if(!result||result.ok!==true||!validate(result.analytics,selected))return failure(result&&result.code||'ETP_ANALYTICS_RESPONSE_INVALID');
    return Object.freeze({ok:true,analytics:result.analytics});
  }
  function render(host,result,options){
    if(!host||!host.ownerDocument)return false;clear(host);var doc=host.ownerDocument,opts=options||{};
    if(!result||result.ok!==true){host.appendChild(text(doc,'strong','ETP verified analytics'));host.appendChild(text(doc,'p',result&&result.code==='ETP_ANALYTICS_STORE_REQUIRED'?'Select one store to view verified figures.':'Verified figures are unavailable — no value is assumed.','etp-e2-note'));return true;}
    var a=result.analytics,heading=text(doc,'div','ETP verified · '+a.view,'etp-e2-heading');host.appendChild(heading);host.appendChild(text(doc,'p',a.verified.banner+' · '+a.period.label+(a.period.partial?' · Partial period':''),'etp-e2-note'));
    var grid=text(doc,'div','', 'etp-e2-grid'),metrics=[['Net sale',money(a.metrics.netSale)],['Bills',number(a.metrics.bills)],['Units',number(a.metrics.units,1000)],['ATV',money(a.metrics.atv)],['UPT',number(a.metrics.upt,1000)],['ASP',money(a.metrics.asp)]];
    metrics.forEach(function(item){var card=text(doc,'div','', 'etp-e2-metric');card.appendChild(text(doc,'span',item[0]));card.appendChild(text(doc,'strong',item[1]));grid.appendChild(card);});host.appendChild(grid);
    host.appendChild(text(doc,'p',a.coverage.label+(a.identity.unassigned===null?'':' · Unassigned '+money(a.identity.unassigned)),'etp-e2-note'));
    if(typeof opts.onOpen==='function'){var button=text(doc,'button','Open verified analytics','etp-e2-open');button.type='button';button.onclick=opts.onOpen;host.appendChild(button);}return true;
  }
  return Object.freeze({VERSION:1,VIEWS:VIEWS,load:load,render:render,validate:validate});
});
