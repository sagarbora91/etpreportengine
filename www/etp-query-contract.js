/* Pure Phase-6B query boundary. It canonicalizes only allowlisted metadata and
   defines opaque cursor bindings; it does not claim to encrypt or authenticate. */
(function(root,factory){
  'use strict';var api=factory();if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpQueryContract=api;
})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var VERSION='ETP_QUERY_V1',MAX_FIELDS=24,MAX_FILTERS=8,MAX_SORTS=3,MAX_IN_VALUES=20,MAX_TEXT=80,MAX_LIMIT=200,MAX_SIGNATURE_INPUT=4096;
  var BLOCKED=Object.freeze(['__proto__','prototype','constructor']);
  function freeze(v){return Object.freeze(v);}function plain(v){if(!v||typeof v!=='object'||Array.isArray(v))return false;var p=Object.getPrototypeOf(v);return p===Object.prototype||p===null;}function exact(v,keys){if(!plain(v))return false;var a=Object.keys(v).sort(),e=keys.slice().sort();return a.length===e.length&&a.every(function(k,i){return k===e[i]&&BLOCKED.indexOf(k)<0;});}
  function list(values){return freeze(values.slice());}
  var PROJECTIONS=freeze({
    R003:list(['invoice_date','transaction_type_raw','net_amount','scheme_discount','user_discount']),
    R013:list(['invoice_date','invoice_number','transaction_type_raw','quantity','net_amount','cro_number']),
    R022:list(['invoice_date','invoice_number','transaction_type_raw','invoice_quantity','net_value','cash_amount','card_amount','bhim_upi_amount','phonepe_amount','paytm_amount','razorpay_amount','bharatpe_amount','cheque_amount','others_amount','payment_type24_amount']),
    R025:list(['invoice_date','invoice_number','transaction_type_raw','quantity','net_amount','brand','cluster','gender','scheme_discount','user_discount','tax_amount'])
  });
  function filters(definition){var out={};Object.keys(definition).forEach(function(field){out[field]=list(definition[field]);});return freeze(out);}
  var FILTERS=freeze({
    R003:filters({invoice_date:['EQ','GTE','LTE'],transaction_type_raw:['EQ','IN']}),
    R013:filters({invoice_date:['EQ','GTE','LTE'],transaction_type_raw:['EQ','IN'],cro_number:['EQ','IN']}),
    R022:filters({invoice_date:['EQ','GTE','LTE'],transaction_type_raw:['EQ','IN']}),
    R025:filters({invoice_date:['EQ','GTE','LTE'],invoice_number:['EQ','IN'],transaction_type_raw:['EQ','IN'],brand:['EQ','IN'],cluster:['EQ','IN'],gender:['EQ','IN']})
  });
  var SORTS=freeze({
    R003:list(['invoice_date','transaction_type_raw','net_amount']),
    R013:list(['invoice_date','invoice_number','cro_number','transaction_type_raw','net_amount','quantity']),
    R022:list(['invoice_date','transaction_type_raw','net_value','invoice_quantity']),
    R025:list(['invoice_date','invoice_number','brand','cluster','gender','transaction_type_raw','net_amount','quantity'])
  });
  function fail(code){return freeze({ok:false,code:code});}
  function iso(value){var raw=String(value||''),m=/^(\d{4})-(\d{2})-(\d{2})$/.exec(raw);if(!m)return'';var d=new Date(Date.UTC(Number(m[1]),Number(m[2])-1,Number(m[3])));return d.getUTCFullYear()===Number(m[1])&&d.getUTCMonth()===Number(m[2])-1&&d.getUTCDate()===Number(m[3])?raw:'';}
  function safeText(value,field){if(typeof value!=='string')return'';var out=value.trim();if(!out||out.length>MAX_TEXT||/[\u0000-\u001f\u007f]/.test(out))return'';if(field==='invoice_date')return iso(out);if(field==='transaction_type_raw'){out=out.toUpperCase();return ['INV','SR','BC'].indexOf(out)>=0?out:'';}return out;}
  function canonicalFilter(item,report){
    if(!exact(item,['field','operator','value']))return null;var field=String(item.field||''),operator=String(item.operator||'').toUpperCase(),allowed=FILTERS[report][field];if(!allowed||allowed.indexOf(operator)<0)return null;
    var value;
    if(operator==='IN'){
      if(!Array.isArray(item.value)||!item.value.length||item.value.length>MAX_IN_VALUES)return null;value=[];
      for(var i=0;i<item.value.length;i++){var normalized=safeText(item.value[i],field);if(!normalized)return null;if(value.indexOf(normalized)<0)value.push(normalized);}value.sort();if(!value.length)return null;value=freeze(value);
    }else{value=safeText(item.value,field);if(!value)return null;}
    return freeze({field:field,operator:operator,value:value});
  }
  function filterKey(item){return item.field+'\u001f'+item.operator+'\u001f'+(Array.isArray(item.value)?item.value.join('\u001e'):item.value);}
  function canonicalize(value){
    if(!exact(value,['contractVersion','reportId','fields','filters','sort','limit','cursor'])||value.contractVersion!==VERSION)return fail('ETP_QUERY_INVALID');
    var report=String(value.reportId||'').toUpperCase(),allowed=PROJECTIONS[report];if(!allowed)return fail('ETP_QUERY_REPORT_INVALID');
    if(!Array.isArray(value.fields)||!value.fields.length||value.fields.length>MAX_FIELDS)return fail('ETP_QUERY_PROJECTION_INVALID');var fields=[],seen={};
    for(var i=0;i<value.fields.length;i++){var field=String(value.fields[i]);if(allowed.indexOf(field)<0||seen[field]||BLOCKED.indexOf(field)>=0)return fail('ETP_QUERY_PROJECTION_INVALID');seen[field]=true;fields.push(field);}
    if(!Array.isArray(value.filters)||value.filters.length>MAX_FILTERS)return fail('ETP_QUERY_FILTER_INVALID');var checkedFilters=[];
    for(i=0;i<value.filters.length;i++){var checked=canonicalFilter(value.filters[i],report);if(!checked)return fail('ETP_QUERY_FILTER_INVALID');checkedFilters.push(checked);}checkedFilters.sort(function(a,b){return filterKey(a).localeCompare(filterKey(b));});
    for(i=1;i<checkedFilters.length;i++)if(filterKey(checkedFilters[i])===filterKey(checkedFilters[i-1]))return fail('ETP_QUERY_FILTER_INVALID');
    if(!Array.isArray(value.sort)||value.sort.length>MAX_SORTS)return fail('ETP_QUERY_SORT_INVALID');var sorts=[],sortSeen={};
    for(i=0;i<value.sort.length;i++){var item=value.sort[i];if(!exact(item,['field','direction']))return fail('ETP_QUERY_SORT_INVALID');field=String(item.field||'');var direction=String(item.direction||'').toUpperCase();if(SORTS[report].indexOf(field)<0||sortSeen[field]||['ASC','DESC'].indexOf(direction)<0)return fail('ETP_QUERY_SORT_INVALID');sortSeen[field]=true;sorts.push(freeze({field:field,direction:direction}));}
    if(!Number.isSafeInteger(value.limit)||value.limit<1||value.limit>MAX_LIMIT)return fail('ETP_QUERY_LIMIT_INVALID');
    if(value.cursor!==null&&!opaqueToken(value.cursor))return fail('ETP_QUERY_CURSOR_INVALID');
    var query=freeze({contractVersion:VERSION,reportId:report,fields:freeze(fields),filters:freeze(checkedFilters),sort:freeze(sorts),limit:value.limit,cursor:value.cursor});
    var signatureInput=JSON.stringify({v:VERSION,r:report,f:fields,w:checkedFilters,s:sorts,l:value.limit});
    if(signatureInput.length>MAX_SIGNATURE_INPUT)return fail('ETP_QUERY_SIGNATURE_INPUT_INVALID');
    return freeze({ok:true,query:query,signatureInput:signatureInput});
  }
  function opaqueToken(value){return typeof value==='string'&&/^cur_[A-Za-z0-9_-]{32,156}$/.test(value);}
  function scopeKey(value){return /^(?:WLMHW|HEMW)\|\d{4}-\d{2}\|\d{4}-\d{2}-\d{2}\.\.\d{4}-\d{2}-\d{2}$/.test(String(value||''));}
  function generation(value){return /^etp_[a-f0-9]{32}$/.test(String(value||''));}
  function binding(value){
    if(!exact(value,['contractVersion','token','scopeKey','generationId','reportId','querySignatureInput'])||value.contractVersion!==VERSION||!opaqueToken(value.token)||!scopeKey(value.scopeKey)||!generation(value.generationId)||!PROJECTIONS[value.reportId]||typeof value.querySignatureInput!=='string'||!value.querySignatureInput.length||value.querySignatureInput.length>MAX_SIGNATURE_INPUT)return fail('ETP_CURSOR_BINDING_INVALID');
    return freeze({ok:true,binding:freeze({contractVersion:VERSION,token:value.token,scopeKey:value.scopeKey,generationId:value.generationId,reportId:value.reportId,querySignatureInput:value.querySignatureInput})});
  }
  function bindingMatches(value,context){var checked=binding(value);if(!checked.ok||!exact(context,['scopeKey','generationId','reportId','querySignatureInput']))return false;var b=checked.binding;return b.scopeKey===context.scopeKey&&b.generationId===context.generationId&&b.reportId===context.reportId&&b.querySignatureInput===context.querySignatureInput;}
  return freeze({VERSION:VERSION,PROJECTIONS:PROJECTIONS,FILTERS:FILTERS,SORTS:SORTS,MAX_FIELDS:MAX_FIELDS,MAX_FILTERS:MAX_FILTERS,MAX_SORTS:MAX_SORTS,MAX_IN_VALUES:MAX_IN_VALUES,MAX_TEXT:MAX_TEXT,MAX_LIMIT:MAX_LIMIT,MAX_SIGNATURE_INPUT:MAX_SIGNATURE_INPUT,canonicalize:canonicalize,isOpaqueToken:opaqueToken,validateCursorBinding:binding,cursorBindingMatches:bindingMatches});
});
