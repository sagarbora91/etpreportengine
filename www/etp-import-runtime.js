/* Shared Retail ETP browser runtime facade. Offline-only; no network capability. */
(function(root,factory){var api=factory(root);if(typeof module==='object'&&module.exports)module.exports=api;if(root)root.SaagarEtpImportRuntimeFactory=api;})(typeof globalThis!=='undefined'?globalThis:this,function(root){
  'use strict';
  var REPORTS=['R003','R013','R022','R025'],CHUNK_ROWS=500,CHUNK_FACT_BYTES=480*1024,MAX_FILES_PER_REPORT=13,MAX_IMPORT_FILES=52;
  function failure(code,stage,detail){return {ok:false,code:code,stage:stage||'RUNTIME',detail:detail||null};}
  function snake(value){return String(value).replace(/([a-z0-9])([A-Z])/g,'$1_$2').toLowerCase();}
  function bytesOf(value){return value instanceof Uint8Array?value:new Uint8Array(value);}
  function readFile(file){
    if(!file)return Promise.reject(new Error('ETP_FILE_REQUIRED'));
    if(typeof file.arrayBuffer==='function')return file.arrayBuffer().then(bytesOf);
    return new Promise(function(resolve,reject){if(typeof root.FileReader!=='function')return reject(new Error('ETP_FILE_READER_UNAVAILABLE'));var reader=new root.FileReader();reader.onerror=function(){reject(new Error('ETP_FILE_READ_FAILED'));};reader.onload=function(){resolve(bytesOf(reader.result));};reader.readAsArrayBuffer(file);});
  }
  function hex(buffer){var out='',view=new Uint8Array(buffer);for(var i=0;i<view.length;i++){var h=view[i].toString(16);out+=h.length<2?'0'+h:h;}return out;}
  function digest(cryptoApi,bytes){if(!cryptoApi||!cryptoApi.subtle||typeof cryptoApi.subtle.digest!=='function')return Promise.reject(new Error('ETP_DIGEST_UNAVAILABLE'));return cryptoApi.subtle.digest('SHA-256',bytes).then(hex);}
  function utf8(value){if(typeof root.TextEncoder==='function')return new root.TextEncoder().encode(String(value));var text=unescape(encodeURIComponent(String(value))),bytes=new Uint8Array(text.length);for(var i=0;i<text.length;i++)bytes[i]=text.charCodeAt(i);return bytes;}
  function appendFactChunks(chunks,reportId,rows){var index=0,part=[];for(var i=0;i<rows.length;i++){var next=part.concat([rows[i]]);if(utf8(JSON.stringify(next)).length>CHUNK_FACT_BYTES){if(!part.length)return false;chunks.push({reportId:reportId,chunkIndex:index++,rows:part});part=[rows[i]];if(utf8(JSON.stringify(part)).length>CHUNK_FACT_BYTES)return false;}else part=next;if(part.length===CHUNK_ROWS){chunks.push({reportId:reportId,chunkIndex:index++,rows:part});part=[];}}if(part.length)chunks.push({reportId:reportId,chunkIndex:index,rows:part});return true;}
  function generationId(cryptoApi){if(!cryptoApi||typeof cryptoApi.getRandomValues!=='function')return '';var bytes=new Uint8Array(16);cryptoApi.getRandomValues(bytes);return 'etp_'+hex(bytes);}
  function exactZero(value){return /^[-+]?0+(?:\.0+)?$/.test(String(value==null?'':value).trim());}
  function allAllowedFields(profile){var seen={},forbidden=/(?:^|_)(?:workbook|worksheet|filename|file_label|file_path|source_name|source_bytes|blob|base64|customer|consumer|mobile|phone|email|address|name|aadhaar|pan|dob)(?:$|_)/i;Object.keys(profile.REPORTS).forEach(function(id){Object.keys(profile.REPORTS[id].fields).forEach(function(raw){var field=snake(profile.REPORTS[id].fields[raw]);if(!forbidden.test(field)&&field!=='payment_type25_amount')seen[field]=true;});});return Object.keys(seen).sort();}
  function create(options){
    options=options||{};var profile=options.profile,profileAuthority=options.profileAuthority,loader=options.loader,workerParser=options.workerParser,lifecycle=options.lifecyclePolicy,coordinatorApi=options.coordinatorApi,nativeApi=options.nativeApi,reconciliation=options.reconciliationPolicy,core=options.coreContract,registryApi=options.controlRegistryApi,readerApi=options.verifiedReaderApi,historyApi=options.importHistoryApi,tenderApi=options.tenderDictionaryApi,cryptoApi=options.crypto||root.crypto;
    if(!profile||!profileAuthority||typeof profileAuthority.authorize!=='function'||!loader||(!workerParser&&options.testOnlySynchronousParser!==true)||!lifecycle||!coordinatorApi||!nativeApi||!reconciliation||!core||!registryApi||!readerApi||!historyApi||typeof historyApi.create!=='function'||!tenderApi||typeof tenderApi.validate!=='function'||profile.VERSION!==core.ETP_CORE_VERSION||profile.VERSION!==profileAuthority.PROFILE_VERSION)return failure('ETP_RUNTIME_DEPENDENCY_INVALID','CREATE');
    var dictionaryChecked=tenderApi.validate(tenderApi.BUILD_DICTIONARY),unknownTender=tenderApi.resolve(tenderApi.BUILD_DICTIONARY,'payment_type24_amount'),quarantinedTender=tenderApi.resolve(tenderApi.BUILD_DICTIONARY,'payment_type25_amount');if(!dictionaryChecked.ok||!unknownTender.ok||unknownTender.classification!=='Unmapped'||unknownTender.mapped!==false||!quarantinedTender.ok||quarantinedTender.classification!=='Quarantined'||quarantinedTender.persisted!==false||JSON.stringify(tenderApi.BUILD_IDENTITY)!==JSON.stringify({contractVersion:dictionaryChecked.dictionary.contractVersion,versionId:dictionaryChecked.dictionary.versionId,effectiveAt:dictionaryChecked.dictionary.effectiveAt}))return failure('ETP_TENDER_DICTIONARY_INVALID','CREATE');
    var tenderIdentity=tenderApi.BUILD_IDENTITY,historyMade=historyApi.create({storage:options.storage||root.localStorage});if(!historyMade||!historyMade.ok)return failure(historyMade&&historyMade.code||'ETP_IMPORT_HISTORY_UNAVAILABLE','CREATE');
    var allowedFields=allAllowedFields(profile),allowedSet={};allowedFields.forEach(function(field){allowedSet[field]=true;});
    var madeStore=nativeApi.create({lifecyclePolicy:lifecycle,plugin:options.plugin,allowedFactFields:allowedFields});
    if(!madeStore||!madeStore.ok)return failure(madeStore&&madeStore.code||'ETP_NATIVE_UNAVAILABLE','CREATE');
    var madeRegistry=registryApi.create({storage:options.storage||root.localStorage,core:core});if(!madeRegistry||!madeRegistry.ok)return failure(madeRegistry&&madeRegistry.code||'ETP_CONTROL_UNAVAILABLE','CREATE');
    var madeReader=readerApi.create({core:core,profile:profile,nativeStore:madeStore.adapter,registry:madeRegistry.registry,lifecyclePolicy:lifecycle});if(!madeReader||!madeReader.ok)return failure(madeReader&&madeReader.code||'ETP_READER_UNAVAILABLE','CREATE');
    var preparedFiles=null,parsedReports=null,pending=null,authorityBinding=null,historyContext=null;
    function appendHistory(outcome){if(!historyContext)return;var value={contractVersion:historyApi.VERSION,eventId:historyContext.generationId+':'+outcome,scopeKey:historyContext.scopeKey,storeCode:historyContext.scope.storeCode,financialYear:historyContext.scope.financialYear,periodStart:historyContext.scope.periodStart,periodEnd:historyContext.scope.periodEnd,outcome:outcome,warningCodes:historyContext.warningCodes,counts:historyContext.counts,actorId:'BUILD_AUTHORIZED_OWNER',occurredAt:new Date().toISOString(),digestRefs:historyContext.digestRefs};historyMade.history.append(value);}
    var pipeline={
      preflight:async function(value){
        if(!value||!Array.isArray(value.files)||value.files.length<REPORTS.length||value.files.length>MAX_IMPORT_FILES)return failure('ETP_FOUR_REPORTS_REQUIRED','PREFLIGHT');
        var counts={},items=[];
        for(var i=0;i<value.files.length;i++){
          var selected=String(value.files[i]&&value.files[i].selectedReportId||'').toUpperCase();
          counts[selected]=(counts[selected]||0)+1;
          if(REPORTS.indexOf(selected)<0||counts[selected]>MAX_FILES_PER_REPORT||!value.files[i].file)return failure('ETP_REPORT_SELECTION_INVALID','PREFLIGHT');
          var bytes;try{bytes=await readFile(value.files[i].file);}catch(error){return failure(String(error&&error.message||'ETP_FILE_READ_FAILED'),'PREFLIGHT');}
          items.push({selectedReportId:selected,fileLabel:String(value.files[i].file.name||selected+'.xlsx'),bytes:bytes});
        }
        if(!REPORTS.every(function(id){return counts[id]>0;}))return failure('ETP_FOUR_REPORTS_REQUIRED','PREFLIGHT');
        preparedFiles=items;return {ok:true,items:items};
      },
      parse:async function(value){
        if(!preparedFiles)return failure('ETP_PREFLIGHT_STATE_MISSING','PARSE');
        if(workerParser){var worked=await workerParser.parse({items:preparedFiles,scope:value.scope,datePolicy:options.datePolicy});if(!worked||!worked.ok)return worked||failure('ETP_WORKER_FAILED','PARSE');parsedReports=worked.reports;return {ok:true,reports:worked.reports};}
        var reports={},sourceHashes={};
        for(var i=0;i<preparedFiles.length;i++){
          var item=preparedFiles[i],loaded=await loader.load({bytes:item.bytes,fileLabel:item.fileLabel,selectedReportId:item.selectedReportId,expectedStoreCode:value.scope.storeCode,datePolicy:options.datePolicy});
          if(!loaded.ok)return loaded;
          var prior=reports[loaded.reportId];
          if(prior&&(prior.signatureKey!==loaded.signatureKey||prior.profileVersion!==loaded.profileVersion||prior.parserVersion!==loaded.parserVersion||prior.storeCode!==loaded.storeCode))return failure('ETP_REPORT_BATCH_MISMATCH','PARSE',{reportId:loaded.reportId});
          sourceHashes[loaded.reportId]=sourceHashes[loaded.reportId]||[];sourceHashes[loaded.reportId].push(await digest(cryptoApi,item.bytes));
          reports[loaded.reportId]=Object.assign({},loaded,{rows:(prior?prior.rows:[]).concat(loaded.rows)});
        }
        if(!REPORTS.every(function(id){return !!reports[id];}))return failure('ETP_FOUR_REPORTS_REQUIRED','PARSE');
        for(var r=0;r<REPORTS.length;r++){var id=REPORTS[r],report=reports[id];report.sourceSha256=await digest(cryptoApi,utf8(sourceHashes[id].slice().sort().join('|')));report.headerSignatureSha256=await digest(cryptoApi,utf8(report.signatureKey));}
        parsedReports=reports;return {ok:true,reports:reports};
      },
      validate:async function(value){
        var reports=value&&value.parsed&&value.parsed.reports;if(!reports||reports!==parsedReports)return failure('ETP_PARSE_STATE_INVALID','VALIDATE');
        var manifest={scopeKey:[value.scope.storeCode,value.scope.financialYear,value.scope.periodStart+'..'+value.scope.periodEnd].join('|'),generationId:options.currentGenerationId(),reports:[]},chunks=[],quarantines={paymentType25Rows:0},scopedReports={},scopeSelection={mode:'EXPLICIT_SCOPE_FILTER',sourceRows:0,selectedRows:0,excludedRows:0,reports:{}};
        for(var r=0;r<REPORTS.length;r++){
          var id=REPORTS[r],loaded=reports[id],factRows=[],selectedRows=[];
          if(loaded.profileVersion!==profileAuthority.PROFILE_VERSION||loaded.parserVersion!==profileAuthority.PARSER_VERSION)return failure('ETP_PROFILE_VERSION_MISMATCH','VALIDATE',{reportId:id});
          for(var n=0;n<loaded.rows.length;n++){
            var row=loaded.rows[n];
            if(row.businessDate<value.scope.periodStart||row.businessDate>value.scope.periodEnd)continue;
            selectedRows.push(row);if(id==='R022'&&row.fields.paymentType25Amount!=null&&row.fields.paymentType25Amount!==''&&!exactZero(row.fields.paymentType25Amount))quarantines.paymentType25Rows++;
            var fact={};Object.keys(row.fields).forEach(function(key){var field=snake(key);if(allowedSet[field])fact[field]=row.fields[key];});factRows.push(fact);
          }
          if(!factRows.length)return failure('ETP_SELECTED_SCOPE_HAS_NO_ROWS','VALIDATE',{reportId:id});
          var excluded=loaded.rows.length-selectedRows.length;scopeSelection.sourceRows+=loaded.rows.length;scopeSelection.selectedRows+=selectedRows.length;scopeSelection.excludedRows+=excluded;scopeSelection.reports[id]={sourceRows:loaded.rows.length,selectedRows:selectedRows.length,excludedRows:excluded};
          scopedReports[id]=Object.assign({},loaded,{rows:selectedRows,rowCount:selectedRows.length});
          manifest.reports.push({reportId:id,sourceSha256:loaded.sourceSha256,headerSignatureSha256:loaded.headerSignatureSha256,rowCount:factRows.length});
          if(!appendFactChunks(chunks,id,factRows))return failure('ETP_FACT_ROW_BYTES_EXCEEDED','VALIDATE',{reportId:id});
        }
        manifest.authority=authorityBinding;manifest.tenderDictionary=tenderIdentity;
        historyContext={generationId:manifest.generationId,scopeKey:manifest.scopeKey,scope:value.scope,warningCodes:quarantines.paymentType25Rows?['PAYMENTTYPE25_QUARANTINED']:[],counts:{sourceCount:scopeSelection.sourceRows,selectedCount:scopeSelection.selectedRows,excludedCount:scopeSelection.excludedRows},digestRefs:manifest.reports.map(function(entry){return 'sha256:'+entry.sourceSha256;})};
        return {ok:true,manifest:manifest,chunks:chunks,reports:scopedReports,quarantines:quarantines,scopeSelection:scopeSelection};
      },
      reconcile:async function(value){
        var reports=value.validated.reports,left=reports.R022.rows.map(function(row){return row.fields;}),right=reports.R025.rows.map(function(row){return row.fields;});
        var checkedCoverage=core.coverage(value.scope,reports,value.coverageDeclaration);if(!checkedCoverage.ok)return checkedCoverage;
        var coverage={left:checkedCoverage.coverage.R022,right:checkedCoverage.coverage.R025},result=reconciliation.compareReports(left,right,core.RECON_RULE,coverage);result.coverage=checkedCoverage.coverage;result.scopeSelection=value.validated.scopeSelection;if(result.status==='PASS'){var attribution=reconciliation.compareReports(reports.R013.rows.map(function(row){return row.fields;}),right,core.ATTRIBUTION_RULE,{left:checkedCoverage.coverage.R013,right:checkedCoverage.coverage.R025}),discount=reconciliation.compareReports(reports.R003.rows.map(function(row){return row.fields;}),right,core.DISCOUNT_RULE,{left:checkedCoverage.coverage.R003,right:checkedCoverage.coverage.R025});result.enrichments={R013:{status:attribution.status,differenceCount:attribution.differences.length},R003:{status:discount.status,differenceCount:discount.differences.length},paymentType25:{status:'QUARANTINED',rowCount:value.validated.quarantines.paymentType25Rows,persisted:false}};}return result;
      },
      authorizePublication:async function(value){if(typeof options.authorizePublication!=='function')return{ok:false};try{return(await options.authorizePublication(value))===true?{ok:true}:{ok:false};}catch(_){return{ok:false};}}
    };
    var currentGeneration='';options.currentGenerationId=function(){return currentGeneration;};
    var made=coordinatorApi.create({lifecyclePolicy:lifecycle,pipeline:pipeline,store:madeStore.adapter});if(!made||!made.ok)return failure(made&&made.code||'ETP_COORDINATOR_UNAVAILABLE','CREATE');
    async function run(request){preparedFiles=null;parsedReports=null;pending=null;authorityBinding=null;historyContext=null;try{var checked=lifecycle.validateScope(request&&request.scope);if(!checked.ok)return failure('ETP_SCOPE_INVALID','SELECT');var authority=profileAuthority.authorize({storeCode:checked.scope.storeCode,purpose:'PRODUCTION',profileVersion:profile.VERSION,parserVersion:profileAuthority.PARSER_VERSION});if(!authority||authority.ok!==true)return failure(authority&&authority.code||'ETP_PROFILE_AUTHORIZATION_REQUIRED','SELECT');authorityBinding=authority.binding;currentGeneration=generationId(cryptoApi);if(!currentGeneration)return failure('ETP_ENTROPY_UNAVAILABLE','SELECT');var nativeStatus=await madeStore.adapter.readStatus(request.scope);if(!nativeStatus.ok)return nativeStatus;var previous=madeRegistry.registry.acceptedLifecycle(checked.key);if(nativeStatus.status.state==='ACCEPTED'&&(!previous||previous.activeGenerationId!==nativeStatus.status.activeGenerationId))return failure('ETP_CONTROL_RECEIPT_REQUIRED','SELECT');if(nativeStatus.status.state!=='ACCEPTED')previous=null;var result=await made.coordinator.run(Object.assign({},request,{generationId:currentGeneration,currentLifecycle:previous}));if(result&&result.awaitingConfirmation){pending={generationId:currentGeneration,reconciliation:result.reconciliation,coverage:result.coverage,authority:authorityBinding,tenderDictionary:tenderIdentity,historyContext:historyContext};appendHistory('VALIDATED');}else if(result&&result.duplicate===true)appendHistory('DUPLICATE_NOOP');else if(result&&!result.ok&&historyContext)appendHistory('REJECTED');if(!result.ok&&result.detail&&result.detail.code&&/^(?:ETP_|XLSX_|RETAIL_)/.test(result.detail.code))return Object.assign({},result,{coordinatorCode:result.code,code:result.detail.code});return result;}finally{preparedFiles=null;parsedReports=null;}}
    async function confirm(life){if(!pending||pending.generationId!==life.candidateGenerationId)return failure('ETP_CONFIRMATION_CONTEXT_MISSING','CONFIRM');var result=await made.coordinator.confirm(life);if(!result.ok){historyContext=pending.historyContext;appendHistory('FAILED_RETRYABLE');return result;}var built=core.createReceipt({lifecycle:result.lifecycle,reconciliation:pending.reconciliation,coverage:pending.coverage,authority:pending.authority,tenderDictionary:pending.tenderDictionary,publishedAtMs:Date.now()});if(!built.ok)return built;var saved=madeRegistry.registry.saveReceipt(built.receipt);if(!saved.ok)return saved;historyContext=pending.historyContext;appendHistory('ACCEPTED');pending=null;return Object.assign({},result,{receipt:built.receipt});}
    return {ok:true,runtime:Object.freeze({run:run,confirm:confirm,readVerified:madeReader.reader.read})};
  }
  function bootstrap(){
    try{
      if(!root.readXlsxFile||!root.fflate||typeof root.fflate.unzipSync!=='function')return failure('ETP_BROWSER_BUNDLE_UNAVAILABLE','BOOTSTRAP');
      var loader=root.SaagarEtpRetailXlsxLoader.create({readWorkbook:function(bytes,settings){return root.readXlsxFile(bytes,settings);},unzipParts:function(bytes){return root.fflate.unzipSync(bytes);}}),worker=root.SaagarEtpWorkerClient.create({Worker:root.Worker,url:'etp-import-worker.js'});if(!worker.ok)return worker;
      return create({profile:root.SaagarEtpRetailProfile,profileAuthority:root.SaagarEtpProfileAuthority,loader:loader,workerParser:worker.client,lifecyclePolicy:root.SaagarEtpStoreLifecyclePolicy,coordinatorApi:root.SaagarEtpImportCoordinator,nativeApi:root.SaagarEtpNativeStore,reconciliationPolicy:root.SaagarEtpReconciliationPolicy,coreContract:root.SaagarEtpCoreContract,controlRegistryApi:root.SaagarEtpControlRegistry,verifiedReaderApi:root.SaagarEtpVerifiedReader,importHistoryApi:root.SaagarEtpImportHistory,tenderDictionaryApi:root.SaagarEtpTenderDictionary,storage:root.localStorage,authorizePublication:async function(){if(typeof root.SaagarReauth!=='function')return false;try{return(await root.SaagarReauth('Publish verified Retail ETP reports'))===true;}catch(_){return false;}},plugin:root.Capacitor&&root.Capacitor.Plugins&&root.Capacitor.Plugins.SaagarEtpStore,crypto:root.crypto,datePolicy:{earliestDate:'2024-04-01',asOfDate:root.SaagarEtpCoreContract.indiaDate(Date.now()),maxFutureDays:2}});
    }catch(_){return failure('ETP_RUNTIME_BOOTSTRAP_FAILED','BOOTSTRAP');}
  }
  var boot=bootstrap();if(boot.ok)root.SaagarEtpImportRuntime=boot.runtime;else root.SaagarEtpImportRuntimeStatus=Object.freeze(boot);
  return Object.freeze({VERSION:1,REPORTS:Object.freeze(REPORTS.slice()),create:create,bootstrap:bootstrap});
});
