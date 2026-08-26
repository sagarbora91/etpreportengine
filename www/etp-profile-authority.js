/* Build-owned ETP profile authority. It has no runtime, storage or user
   authorization surface; changing a store decision requires new product bytes. */
(function(root,factory){'use strict';var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpProfileAuthority=api;})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VERSION='ETP_PROFILE_AUTHORITY_V1',PROFILE_VERSION='retail-etp-core-v1',PARSER_VERSION='retail-etp-parser-v1',AUTHORITY_ID='phase6b-profile-authority-2026-08-24-v1';
  var STORES=Object.freeze({WLMHW:Object.freeze({status:'PRODUCTION_AUTHORIZED',evidenceIdentity:'WLMHW_PROFILE_EVIDENCE_2026_08_24_V1'}),HEMW:Object.freeze({status:'EVIDENCE_PENDING',evidenceIdentity:'HEMW_PROFILE_EVIDENCE_PENDING_V1'})});
  function freeze(value){return Object.freeze(value);}function plain(value){if(!value||typeof value!=='object'||Array.isArray(value))return false;var proto=Object.getPrototypeOf(value);return proto===Object.prototype||proto===null;}function fail(code){return freeze({ok:false,code:code});}
  function authorize(value){
    if(!plain(value)||Object.keys(value).sort().join('|')!=='parserVersion|profileVersion|purpose|storeCode')return fail('ETP_PROFILE_AUTHORITY_REQUEST_INVALID');
    var store=String(value.storeCode||'').toUpperCase(),purpose=String(value.purpose||'').toUpperCase(),policy=STORES[store];
    if(value.profileVersion!==PROFILE_VERSION||value.parserVersion!==PARSER_VERSION)return fail('ETP_PROFILE_VERSION_MISMATCH');
    if(!policy||['PRODUCTION','AGGREGATE_EVIDENCE'].indexOf(purpose)<0)return fail('ETP_PROFILE_AUTHORITY_REQUEST_INVALID');
    if(store==='HEMW'&&purpose==='PRODUCTION')return fail('ETP_HEMW_PROFILE_AUTHORIZATION_REQUIRED');
    return freeze({ok:true,productionReady:store==='WLMHW'&&purpose==='PRODUCTION',binding:freeze({contractVersion:VERSION,authorityId:AUTHORITY_ID,storeCode:store,status:policy.status,purpose:purpose,profileVersion:PROFILE_VERSION,parserVersion:PARSER_VERSION,evidenceIdentity:policy.evidenceIdentity})});
  }
  return freeze({VERSION:VERSION,PROFILE_VERSION:PROFILE_VERSION,PARSER_VERSION:PARSER_VERSION,AUTHORITY_ID:AUTHORITY_ID,STORES:STORES,authorize:authorize});
});
