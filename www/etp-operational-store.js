/* V6 ETP: bounded repository for portable human-action overlays. */
(function(root,factory){'use strict';var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpOperationalStore=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VERSION='ETP_OPERATIONAL_STORE_V1',MAX_ITEMS=500,MAX_HISTORY=2000,DOMAINS=['E3','E4','E6','E5','E7'];
  var BAD=/^(?:__proto__|prototype|constructor|sourcefacts|rows|rawrows|workbook|customer|customername|customermobile|phone|email|address|aadhaar|pan)$/i;
  function freeze(v){if(v&&typeof v==='object'&&!Object.isFrozen(v)){Object.keys(v).forEach(function(k){freeze(v[k]);});Object.freeze(v);}return v;}
  function clone(v){return JSON.parse(JSON.stringify(v));}
  function rec(v){return !!v&&typeof v==='object'&&!Array.isArray(v);}
  function exact(v,keys){return rec(v)&&Object.keys(v).sort().join('|')===keys.slice().sort().join('|');}
  function safe(v,re){return typeof v==='string'&&re.test(v)?v:null;}
  function time(v){var n=Date.parse(v);return typeof v==='string'&&isFinite(n)&&/^\d{4}-\d{2}-\d{2}T/.test(v)?new Date(n).toISOString():null;}
  function fail(code){return freeze({ok:false,code:code});}
  function canonical(v){if(Array.isArray(v))return '['+v.map(canonical).join(',')+']';if(rec(v))return '{'+Object.keys(v).sort().map(function(k){return JSON.stringify(k)+':'+canonical(v[k]);}).join(',')+'}';return JSON.stringify(v);}
  function hostile(v){if(Array.isArray(v))return v.some(hostile);if(!rec(v))return false;return Object.keys(v).some(function(k){return BAD.test(k)||hostile(v[k]);});}
  function scope(v){var m=typeof v==='string'&&/^(WLMHW|HEMW)\|([0-9]{4}-[0-9]{2})\|(\d{4}-\d{2}-\d{2})\.\.(\d{4}-\d{2}-\d{2})$/.exec(v);return m&&m[3]<=m[4]?{storeCode:m[1],financialYear:m[2],scopeKey:v}:null;}
  function restoredState(v){if(v===null)return null;if(!exact(v,['contractVersion','restoredAt','overlays','scopes'])||!time(v.restoredAt)||!Array.isArray(v.overlays)||!Array.isArray(v.scopes)||v.overlays.length>MAX_ITEMS)return false;var seen={};for(var i=0;i<v.scopes.length;i++){var x=v.scopes[i];if(!exact(x,['scopeKey','status','generationId','receiptId'])||!scope(x.scopeKey)||seen[x.scopeKey]||['FENCED_REIMPORT_REQUIRED','BOUND_VERIFIED'].indexOf(x.status)<0)return false;seen[x.scopeKey]=true;if(x.status==='FENCED_REIMPORT_REQUIRED'&&(x.generationId!==null||x.receiptId!==null))return false;if(x.status==='BOUND_VERIFIED'&&(!safe(x.generationId,/^etp_[a-f0-9]{32}$/)||!safe(x.receiptId,/^[A-Za-z0-9][A-Za-z0-9._:-]{2,95}$/)))return false;}return v.scopes.length>0?v:false;}
  function input(v){
    if(!exact(v,['domain','storeCode','financialYear','scopeKey','overlayId','updatedAt','payload'])||DOMAINS.indexOf(v.domain)<0||!safe(v.overlayId,/^[A-Za-z0-9][A-Za-z0-9._:-]{2,95}$/)||!time(v.updatedAt)||!rec(v.payload)||hostile(v.payload))return null;
    var s=scope(v.scopeKey);if(!s||s.storeCode!==v.storeCode||s.financialYear!==v.financialYear)return null;
    return {domain:v.domain,storeCode:s.storeCode,financialYear:s.financialYear,scopeKey:s.scopeKey,overlayId:v.overlayId,updatedAt:time(v.updatedAt),payload:clone(v.payload)};
  }
  function create(foundation,serialized){
    if(!foundation||typeof foundation.createOverlay!=='function'||typeof foundation.createPortableBackup!=='function'||typeof foundation.restorePortableBackup!=='function'||typeof foundation.rebindVerifiedScope!=='function'||typeof foundation.canReadVerifiedScope!=='function')return fail('ETP_STORE_FOUNDATION_REQUIRED');
    var items={},audit=[],restored=null;
    function key(v){return v.storeCode+'|'+v.financialYear+'|'+v.scopeKey+'|'+v.domain+'|'+v.overlayId;}
    function event(type,item,revision,at,sequence){return freeze({sequence:sequence||audit.length+1,type:type,key:key(item),domain:item.domain,storeCode:item.storeCode,financialYear:item.financialYear,scopeKey:item.scopeKey,overlayId:item.overlayId,revision:revision,at:at});}
    function put(value,expectedRevision){
      var clean=input(value),prior,k,foundationOverlay;if(!clean)return fail('ETP_STORE_OVERLAY_INVALID');k=key(clean);prior=items[k]||null;
      if(!Number.isSafeInteger(expectedRevision)||expectedRevision<0)return fail('ETP_STORE_REVISION_INVALID');
      if((prior?prior.revision:0)!==expectedRevision)return fail('ETP_STORE_REVISION_CONFLICT');
      foundationOverlay=foundation.createOverlay({domain:clean.domain,storeCode:clean.storeCode,scopeKey:clean.scopeKey,overlayId:clean.overlayId,updatedAt:clean.updatedAt,payload:clean.payload});
      if(!foundationOverlay||foundationOverlay.ok!==true)return fail('ETP_STORE_OVERLAY_INVALID');
      if(prior&&canonical(prior.overlay)===canonical(foundationOverlay.overlay))return freeze({ok:true,changed:false,revision:prior.revision,overlay:prior.overlay});
      if(!prior&&Object.keys(items).length>=MAX_ITEMS)return fail('ETP_STORE_LIMIT_EXCEEDED');if(audit.length>=MAX_HISTORY)return fail('ETP_STORE_HISTORY_LIMIT_EXCEEDED');
      var revision=expectedRevision+1;items[k]={revision:revision,overlay:foundationOverlay.overlay,financialYear:clean.financialYear};audit.push(event(prior?'UPDATED':'CREATED',clean,revision,clean.updatedAt));
      return freeze({ok:true,changed:true,revision:revision,overlay:foundationOverlay.overlay});
    }
    function find(query){
      if(!exact(query,['domain','storeCode','financialYear','scopeKey','overlayId']))return fail('ETP_STORE_QUERY_INVALID');var clean=input(Object.assign({},query,{updatedAt:'2000-01-01T00:00:00.000Z',payload:{state:'QUERY'}}));if(!clean)return fail('ETP_STORE_QUERY_INVALID');var found=items[key(clean)];return found?freeze({ok:true,found:true,revision:found.revision,overlay:found.overlay}):freeze({ok:true,found:false,revision:0,overlay:null});
    }
    function list(filter){
      if(!exact(filter,['domain','storeCode','financialYear','scopeKey'])||DOMAINS.indexOf(filter.domain)<0)return fail('ETP_STORE_QUERY_INVALID');var s=scope(filter.scopeKey);if(!s||s.storeCode!==filter.storeCode||s.financialYear!==filter.financialYear)return fail('ETP_STORE_QUERY_INVALID');
      var out=Object.keys(items).map(function(k){return items[k];}).filter(function(x){var o=x.overlay;return o.domain===filter.domain&&o.storeCode===filter.storeCode&&x.financialYear===filter.financialYear&&o.scopeKey===filter.scopeKey;}).sort(function(a,b){return a.overlay.overlayId.localeCompare(b.overlay.overlayId);}).map(function(x){return freeze({revision:x.revision,overlay:x.overlay});});return freeze({ok:true,items:out});
    }
    function portableE3(overlay){var payload=clone(overlay.payload),record=payload&&payload.record,parsed,day,assignments={},audit;
      if(!payload||payload.kind!=='E3_ORCHESTRATOR_STATE'||!record||typeof record.day!=='string')return overlay;
      try{parsed=JSON.parse(record.day);}catch(_){return null;}day=parsed&&parsed.day;
      if(!rec(parsed)||parsed.schemaVersion!==1||parsed.version!=='ETP_CRO_RECONCILIATION_V1'||!rec(day)||!Array.isArray(day.declarations)||!Array.isArray(day.audit)||!Array.isArray(day.dispositions))return null;
      day.declarations.forEach(function(x){if(rec(x)&&typeof x.invoiceId==='string'&&typeof x.croId==='string')assignments[x.invoiceId]=x.croId;});
      audit=day.audit.filter(function(x){return rec(x)&&['INVOICE_DECLARED','STATE_TRANSITION','ATTRIBUTION_CORRECTED','VARIANCE_DISPOSED'].indexOf(x.event)>=0;}).map(function(x,i){var clean=clone(x);clean.sequence=i+1;if(clean.event==='ATTRIBUTION_CORRECTED'){clean.before=null;if(rec(clean.after)&&typeof clean.after.invoiceId==='string'&&typeof clean.after.croId==='string')assignments[clean.after.invoiceId]=clean.after.croId;}return clean;});
      day.sourceFacts=[];day.assignments=assignments;day.outcomes=[];day.unassignedQueue=[];day.audit=audit;record.day=JSON.stringify(parsed);
      return foundation.createOverlay({domain:overlay.domain,storeCode:overlay.storeCode,scopeKey:overlay.scopeKey,overlayId:overlay.overlayId,updatedAt:overlay.updatedAt,payload:payload}).overlay||null;
    }
    function exportPortable(createdAt){var overlays=Object.keys(items).sort().map(function(k){var overlay=items[k].overlay;return overlay.domain==='E3'?portableE3(overlay):overlay;});if(overlays.some(function(x){return !x;}))return fail('ETP_STORE_PORTABLE_INVALID');return overlays.length?foundation.createPortableBackup(overlays,createdAt):fail('ETP_STORE_EMPTY');}
    function ingest(backup,restoredAt){var result=foundation.restorePortableBackup(backup,restoredAt);if(!result||result.ok!==true)return fail('ETP_STORE_RESTORE_INVALID');var staged={},stagedAudit=[];
      for(var i=0;i<result.restored.overlays.length;i++){var o=result.restored.overlays[i],s=scope(o.scopeKey),clean=input({domain:o.domain,storeCode:o.storeCode,financialYear:s&&s.financialYear,scopeKey:o.scopeKey,overlayId:o.overlayId,updatedAt:o.updatedAt,payload:o.payload});if(!clean)return fail('ETP_STORE_RESTORE_INVALID');staged[key(clean)]={revision:1,overlay:o,financialYear:clean.financialYear};stagedAudit.push(event('RESTORED',clean,1,time(restoredAt),stagedAudit.length+1));}
      items=staged;audit=stagedAudit;restored=result.restored;return freeze({ok:true,count:Object.keys(items).length,scopes:restored.scopes});
    }
    function rebind(binding){if(!restored)return fail('ETP_STORE_NOT_RESTORED');var result=foundation.rebindVerifiedScope(restored,binding);if(!result||result.ok!==true)return result||fail('ETP_STORE_REBIND_INVALID');restored=result.restored;return freeze({ok:true,scopes:restored.scopes});}
    function canRead(scopeKey,binding){return restored===null||foundation.canReadVerifiedScope(restored,scopeKey,binding);}
    function serialize(){var body={contractVersion:VERSION,items:Object.keys(items).sort().map(function(k){return {revision:items[k].revision,financialYear:items[k].financialYear,overlay:items[k].overlay};}),audit:audit.slice(),restored:restored};return canonical(body);}
    function load(raw){var parsed;try{parsed=JSON.parse(raw);}catch(_){return fail('ETP_STORE_SERIALIZED_INVALID');}if(!exact(parsed,['contractVersion','items','audit','restored'])||parsed.contractVersion!==VERSION||!Array.isArray(parsed.items)||!Array.isArray(parsed.audit)||parsed.items.length>MAX_ITEMS||parsed.audit.length>MAX_HISTORY)return fail('ETP_STORE_SERIALIZED_INVALID');var rebuilt={};
      for(var i=0;i<parsed.items.length;i++){var x=parsed.items[i],o=x&&x.overlay,s=o&&scope(o.scopeKey),clean=input(o&&{domain:o.domain,storeCode:o.storeCode,financialYear:x.financialYear,scopeKey:o.scopeKey,overlayId:o.overlayId,updatedAt:o.updatedAt,payload:o.payload});if(!exact(x,['revision','financialYear','overlay'])||!clean||!Number.isSafeInteger(x.revision)||x.revision<1||!s)return fail('ETP_STORE_SERIALIZED_INVALID');var checked=foundation.createOverlay(o);if(!checked.ok||rebuilt[key(clean)])return fail('ETP_STORE_SERIALIZED_INVALID');rebuilt[key(clean)]={revision:x.revision,financialYear:x.financialYear,overlay:checked.overlay};}
      var checkedRestored=restoredState(parsed.restored);if(checkedRestored===false||parsed.audit.some(function(e,i){return !exact(e,['sequence','type','key','domain','storeCode','financialYear','scopeKey','overlayId','revision','at'])||e.sequence!==i+1||['CREATED','UPDATED','RESTORED'].indexOf(e.type)<0||DOMAINS.indexOf(e.domain)<0||!scope(e.scopeKey)||e.storeCode!==scope(e.scopeKey).storeCode||e.financialYear!==scope(e.scopeKey).financialYear||!Number.isSafeInteger(e.revision)||e.revision<1||!time(e.at)||!safe(e.key,/^[A-Za-z0-9|._:-]{3,400}$/);}))return fail('ETP_STORE_SERIALIZED_INVALID');items=rebuilt;audit=parsed.audit.map(freeze);restored=checkedRestored;return freeze({ok:true,count:Object.keys(items).length});
    }
    var api=freeze({VERSION:VERSION,put:put,get:find,list:list,history:function(){return freeze({ok:true,events:audit.slice()});},exportPortable:exportPortable,ingestRestore:ingest,rebindVerifiedScope:rebind,canReadVerifiedScope:canRead,serialize:serialize,load:load});if(serialized!==undefined){var loaded=load(serialized);if(!loaded.ok)return loaded;}return freeze({ok:true,store:api});
  }
  return freeze({VERSION:VERSION,MAX_ITEMS:MAX_ITEMS,MAX_HISTORY:MAX_HISTORY,create:create});
});
