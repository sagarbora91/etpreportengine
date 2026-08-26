/* Bounded browser worker client for ETP parsing. No file names, cells or parser
   exceptions are copied into diagnostics. */
(function(root,factory){var api=factory(root);if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpWorkerClient=api;})(typeof globalThis!=='undefined'?globalThis:this,function(root){
  'use strict';var TIMEOUT_MS=120000;
  function create(options){var WorkerCtor=options&&options.Worker||root.Worker,url=options&&options.url||'etp-import-worker.js',timeout=options&&options.timeoutMs||TIMEOUT_MS;if(typeof WorkerCtor!=='function'||!Number.isSafeInteger(timeout)||timeout<1000||timeout>300000)return{ok:false,code:'ETP_WORKER_UNAVAILABLE'};
    async function parse(input){return new Promise(function(resolve){var worker,timer,settled=false;function finish(value){if(settled)return;settled=true;if(timer)clearTimeout(timer);if(worker)worker.terminate();resolve(value);}try{worker=new WorkerCtor(url);worker.onmessage=function(event){var value=event&&event.data;finish(value&&typeof value==='object'?value:{ok:false,code:'ETP_WORKER_RESPONSE_INVALID'});};worker.onerror=function(){finish({ok:false,code:'ETP_WORKER_FAILED'});};timer=setTimeout(function(){finish({ok:false,code:'XLSX_TIMEOUT'});},timeout);var transfer=[];input.items.forEach(function(item){if(item.bytes&&item.bytes.buffer)transfer.push(item.bytes.buffer);});worker.postMessage({type:'PARSE_REPORT_BATCH',items:input.items,scope:input.scope,datePolicy:input.datePolicy},transfer);}catch(_){finish({ok:false,code:'ETP_WORKER_FAILED'});}});}
    return{ok:true,client:Object.freeze({parse:parse})};
  }
  return Object.freeze({VERSION:1,TIMEOUT_MS:TIMEOUT_MS,create:create});
});
