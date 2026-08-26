/* V6 K.1: fail-closed authority, portable-overlay and restore-fence contract for E3-E7. */
(function(root,factory){'use strict';var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpOperationalFoundation=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VERSION='ETP_OPERATIONAL_FOUNDATION_V1',DOMAINS=Object.freeze(['E3','E4','E6','E5','E7']),ROLES=Object.freeze(['Owner','Store Manager','Staff']),MAX_OVERLAYS=500,MAX_PAYLOAD_BYTES=65536,FRESH_MS=5*60*1000;
  var ACTIONS=Object.freeze({
    E3:Object.freeze({DECLARE:['Staff','Store Manager','Owner'],CLOSE:['Store Manager','Owner'],IMPORT:['Store Manager','Owner'],RECONCILE:['Store Manager','Owner'],CORRECT:['Store Manager','Owner'],CORRECT_AFTER_FREEZE:['Owner'],DISPOSE:['Store Manager','Owner'],LOCK:['Store Manager','Owner']}),
    E4:Object.freeze({PUBLISH:['Owner'],REVISE:['Owner'],REALLOCATE:['Owner']}),
    E6:Object.freeze({ACKNOWLEDGE:['Store Manager','Owner'],REASSIGN:['Owner'],CLOSE:['Store Manager','Owner']}),
    E5:Object.freeze({ACTIVATE_SCHEME:['Owner'],FINALIZE:['Owner'],PAYROLL_PRELOCK:['Owner']}),
    E7:Object.freeze({ACTIVATE_PROFILE:['Owner'],VERIFY:['Store Manager','Owner']})
  });
  var PRIVILEGED=Object.freeze(['CLOSE','IMPORT','RECONCILE','CORRECT','CORRECT_AFTER_FREEZE','DISPOSE','LOCK','PUBLISH','REVISE','REALLOCATE','ACKNOWLEDGE','REASSIGN','ACTIVATE_SCHEME','FINALIZE','PAYROLL_PRELOCK','ACTIVATE_PROFILE','VERIFY']);
  var FORBIDDEN=Object.freeze(['sourceFacts','rows','rawRows','workbook','customer','customerName','customerMobile','phone','email','address','aadhaar','pan']);
  function freeze(v){if(v&&typeof v==='object'&&!Object.isFrozen(v)){Object.keys(v).forEach(function(k){freeze(v[k]);});Object.freeze(v);}return v;}
  function fail(code){return freeze({ok:false,code:code});}
  function record(v){return !!v&&typeof v==='object'&&!Array.isArray(v);}
  function exact(v,keys){return record(v)&&Object.keys(v).sort().join('|')===keys.slice().sort().join('|');}
  function safe(v,re){return typeof v==='string'&&re.test(v)?v:null;}
  function timestamp(v){var n=Date.parse(v);return typeof v==='string'&&isFinite(n)&&/^\d{4}-\d{2}-\d{2}T/.test(v)?new Date(n).toISOString():null;}
  function canonical(v){if(Array.isArray(v))return '['+v.map(canonical).join(',')+']';if(record(v))return '{'+Object.keys(v).sort().map(function(k){return JSON.stringify(k)+':'+canonical(v[k]);}).join(',')+'}';return JSON.stringify(v);}
  function hash(s){var h=2166136261,i;for(i=0;i<s.length;i++){h^=s.charCodeAt(i);h=Math.imul(h,16777619);}return ('00000000'+(h>>>0).toString(16)).slice(-8);}
  function hasForbidden(v){if(Array.isArray(v))return v.some(hasForbidden);if(!record(v))return false;return Object.keys(v).some(function(k){return FORBIDDEN.indexOf(k)>=0||hasForbidden(v[k]);});}
  function authority(input){
    if(!exact(input,['domain','status','sourceSha256','approvalId','approvedAt','approvedByRole','stores'])||DOMAINS.indexOf(input.domain)<0||input.status!=='ACTIVE'||input.approvedByRole!=='Owner'||!safe(input.sourceSha256,/^[a-f0-9]{64}$/)||!safe(input.approvalId,/^[A-Z0-9][A-Z0-9._-]{2,63}$/)||!timestamp(input.approvedAt)||!Array.isArray(input.stores)||!input.stores.length)return fail('ETP_AUTHORITY_NOT_ACTIVE');
    var stores=[];for(var i=0;i<input.stores.length;i++){var store=safe(input.stores[i],/^(?:WLMHW|HEMW)$/);if(!store||stores.indexOf(store)>=0)return fail('ETP_AUTHORITY_INVALID');stores.push(store);}
    return freeze({ok:true,authority:{contractVersion:VERSION,domain:input.domain,status:'ACTIVE',sourceSha256:input.sourceSha256,approvalId:input.approvalId,approvedAt:timestamp(input.approvedAt),approvedByRole:'Owner',stores:stores}});
  }
  function authorize(active,request){
    if(!active||active.ok!==true||!record(active.authority)||!exact(request,['domain','action','actorId','actorRole','storeCode','at','reauthenticatedAt']))return fail('ETP_OPERATION_DENIED');
    var a=active.authority,at=timestamp(request.at),reauth=timestamp(request.reauthenticatedAt),roles=ACTIONS[request.domain]&&ACTIONS[request.domain][request.action];
    if(a.contractVersion!==VERSION||a.status!=='ACTIVE'||a.domain!==request.domain||!roles||!safe(request.actorId,/^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/)||ROLES.indexOf(request.actorRole)<0||roles.indexOf(request.actorRole)<0||a.stores.indexOf(request.storeCode)<0||!at)return fail('ETP_OPERATION_DENIED');
    if(PRIVILEGED.indexOf(request.action)>=0&&(!reauth||Date.parse(reauth)>Date.parse(at)||Date.parse(at)-Date.parse(reauth)>FRESH_MS))return fail('ETP_FRESH_REAUTH_REQUIRED');
    return freeze({ok:true,grant:{contractVersion:VERSION,domain:request.domain,action:request.action,actorId:request.actorId,actorRole:request.actorRole,storeCode:request.storeCode,at:at,reauthenticatedAt:reauth,authorityApprovalId:a.approvalId,grantId:'ETP-GRANT-'+hash(canonical(request)+'|'+a.approvalId)}});
  }
  function overlay(input){
    var fields=['domain','storeCode','scopeKey','overlayId','updatedAt','payload'];if(record(input)&&input.contractVersion===VERSION)fields.push('contractVersion');
    if(!exact(input,fields)||DOMAINS.indexOf(input.domain)<0||!safe(input.storeCode,/^(?:WLMHW|HEMW)$/)||!safe(input.scopeKey,/^(?:WLMHW|HEMW)\|[^|]{3,20}\|\d{4}-\d{2}-\d{2}\.\.\d{4}-\d{2}-\d{2}$/)||input.scopeKey.indexOf(input.storeCode+'|')!==0||!safe(input.overlayId,/^[A-Za-z0-9][A-Za-z0-9._:-]{2,95}$/)||!timestamp(input.updatedAt)||!record(input.payload)||hasForbidden(input.payload))return null;
    var payload=JSON.parse(JSON.stringify(input.payload));if(canonical(payload).length>MAX_PAYLOAD_BYTES)return null;
    return freeze({contractVersion:VERSION,domain:input.domain,storeCode:input.storeCode,scopeKey:input.scopeKey,overlayId:input.overlayId,updatedAt:timestamp(input.updatedAt),payload:payload});
  }
  function portableBackup(items,createdAt){
    var at=timestamp(createdAt);if(!Array.isArray(items)||!items.length||items.length>MAX_OVERLAYS||!at)return fail('ETP_OVERLAY_BACKUP_INVALID');var clean=[],seen={};
    for(var i=0;i<items.length;i++){var item=overlay(items[i]);if(!item||seen[item.domain+'|'+item.overlayId])return fail('ETP_OVERLAY_BACKUP_INVALID');seen[item.domain+'|'+item.overlayId]=true;clean.push(item);}
    clean.sort(function(a,b){return a.domain.localeCompare(b.domain)||a.overlayId.localeCompare(b.overlayId);});var basis={contractVersion:VERSION,kind:'ETP_HUMAN_ACTION_OVERLAYS',createdAt:at,overlays:clean};
    return freeze({ok:true,backup:Object.assign({},basis,{checksum:'ETP-OVERLAY-'+hash(canonical(basis))})});
  }
  function restore(input,restoredAt){
    if(!record(input)||input.contractVersion!==VERSION||input.kind!=='ETP_HUMAN_ACTION_OVERLAYS'||!Array.isArray(input.overlays)||!timestamp(input.createdAt)||!safe(input.checksum,/^ETP-OVERLAY-[a-f0-9]{8}$/))return fail('ETP_OVERLAY_RESTORE_INVALID');
    var rebuilt=portableBackup(input.overlays,input.createdAt);if(!rebuilt.ok||rebuilt.backup.checksum!==input.checksum||!timestamp(restoredAt))return fail('ETP_OVERLAY_RESTORE_INVALID');
    return freeze({ok:true,restored:{contractVersion:VERSION,restoredAt:timestamp(restoredAt),overlays:rebuilt.backup.overlays,scopes:rebuilt.backup.overlays.map(function(x){return x.scopeKey;}).filter(function(x,i,a){return a.indexOf(x)===i;}).sort().map(function(scopeKey){return freeze({scopeKey:scopeKey,status:'FENCED_REIMPORT_REQUIRED',generationId:null,receiptId:null});})}});
  }
  function rebind(restored,binding){
    if(!record(restored)||restored.contractVersion!==VERSION||!Array.isArray(restored.scopes)||!exact(binding,['source','scopeKey','generationId','receiptId'])||binding.source!=='ETP_VERIFIED'||!safe(binding.generationId,/^etp_[a-f0-9]{32}$/)||!safe(binding.receiptId,/^[A-Za-z0-9][A-Za-z0-9._:-]{2,95}$/))return fail('ETP_REBIND_INVALID');
    var found=false,scopes=restored.scopes.map(function(item){if(item.scopeKey!==binding.scopeKey)return item;found=true;return freeze({scopeKey:item.scopeKey,status:'BOUND_VERIFIED',generationId:binding.generationId,receiptId:binding.receiptId});});if(!found)return fail('ETP_REBIND_SCOPE_UNKNOWN');
    return freeze({ok:true,restored:Object.assign({},restored,{scopes:scopes})});
  }
  function canRead(restored,scopeKey,binding){if(!record(restored)||!Array.isArray(restored.scopes)||!record(binding))return false;return restored.scopes.some(function(x){return x.scopeKey===scopeKey&&x.status==='BOUND_VERIFIED'&&x.generationId===binding.generationId&&x.receiptId===binding.receiptId;});}
  return freeze({VERSION:VERSION,DOMAINS:DOMAINS,ROLES:ROLES,ACTIONS:ACTIONS,registerAuthority:authority,authorize:authorize,createOverlay:function(v){var out=overlay(v);return out?freeze({ok:true,overlay:out}):fail('ETP_OVERLAY_INVALID');},createPortableBackup:portableBackup,restorePortableBackup:restore,rebindVerifiedScope:rebind,canReadVerifiedScope:canRead});
});
