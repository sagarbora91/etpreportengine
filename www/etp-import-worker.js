/* Dedicated offline ETP parser worker. Do not log workbook or row material. */
'use strict';
importScripts('vendor/fflate-0.8.3.min.js','vendor/read-excel-file-9.3.7.min.js','etp-import-foundation.js','etp-xlsx-preflight.js','etp-xlsx-parser-policy.js','etp-retail-profile.js','etp-retail-table-parser.js','etp-retail-xlsx-loader.js');
(function(){
  function hex(buffer){var out='',view=new Uint8Array(buffer);for(var i=0;i<view.length;i++){var h=view[i].toString(16);out+=h.length<2?'0'+h:h;}return out;}
  function digest(bytes){return crypto.subtle.digest('SHA-256',bytes).then(hex);}
  function utf8(value){
    var text=String(value);
    if(typeof TextEncoder==='function')return new TextEncoder().encode(text);
    var encoded=unescape(encodeURIComponent(text));
    var bytes=new Uint8Array(encoded.length);
    for(var i=0;i<encoded.length;i+=1)bytes[i]=encoded.charCodeAt(i);
    return bytes;
  }
  self.onmessage=async function(event){var value=event&&event.data;if(!value||value.type!=='PARSE_REPORT_BATCH'||!Array.isArray(value.items)||value.items.length<4||value.items.length>52){self.postMessage({ok:false,code:'ETP_WORKER_REQUEST_INVALID'});return;}try{var loader=SaagarEtpRetailXlsxLoader.create({readWorkbook:function(bytes,settings){return readXlsxFile(bytes,settings);},unzipParts:function(bytes){return fflate.unzipSync(bytes);}}),reports={},hashes={},counts={};for(var i=0;i<value.items.length;i++){var item=value.items[i],selected=String(item&&item.selectedReportId||'').toUpperCase();counts[selected]=(counts[selected]||0)+1;if(['R003','R013','R022','R025'].indexOf(selected)<0||counts[selected]>13){self.postMessage({ok:false,code:'ETP_WORKER_REQUEST_INVALID'});return;}var loaded=await loader.load({bytes:item.bytes,fileLabel:item.fileLabel,selectedReportId:selected,expectedStoreCode:value.scope.storeCode,datePolicy:value.datePolicy});if(!loaded.ok){self.postMessage({ok:false,code:loaded.code});return;}var prior=reports[loaded.reportId];if(prior&&(prior.signatureKey!==loaded.signatureKey||prior.profileVersion!==loaded.profileVersion||prior.parserVersion!==loaded.parserVersion||prior.storeCode!==loaded.storeCode)){self.postMessage({ok:false,code:'ETP_REPORT_BATCH_MISMATCH'});return;}hashes[loaded.reportId]=hashes[loaded.reportId]||[];hashes[loaded.reportId].push(await digest(item.bytes));reports[loaded.reportId]=Object.assign({},loaded,{rows:(prior?prior.rows:[]).concat(loaded.rows)});}var required=['R003','R013','R022','R025'];for(var r=0;r<required.length;r++){var id=required[r],report=reports[id];if(!report){self.postMessage({ok:false,code:'ETP_FOUR_REPORTS_REQUIRED'});return;}reports[id]=Object.freeze(Object.assign({},report,{sourceSha256:await digest(utf8(hashes[id].slice().sort().join('|'))),headerSignatureSha256:await digest(utf8(report.signatureKey)),rows:Object.freeze(report.rows.slice())}));}self.postMessage({ok:true,reports:reports});}catch(_){self.postMessage({ok:false,code:'ETP_WORKER_FAILED'});}}
})();
