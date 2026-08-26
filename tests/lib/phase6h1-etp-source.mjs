/* Deterministic reversal of the additive Phase 6H.1 ETP analytics wiring. */
export function restorePrePhase6h1EtpIndex(source) {
  return String(source)
    .replace(/  <link rel="stylesheet" href="\.\.\/\.\.\/etp-e3-presentation\.css">\r?\n/, '')
    .replace(/  <link rel="stylesheet" href="\.\.\/\.\.\/etp-e[4567]-presentation\.css">\r?\n/g, '')
    .replace(/  <link rel="stylesheet" href="\.\.\/\.\.\/etp-operational-i18n\.css">\r?\n/, '')
    .replace(/  <script src="\.\.\/\.\.\/etp-operational-i18n\.js"><\/script>\r?\n/, '')
    .replace('  <script src="../../etp-operational-module-host.js"></script>\n', '')
    .replace('  <script src="../../etp-operational-frame-bridge.js"></script>\n', '')
    .replace('  <script src="../../etp-e7-presentation.js"></script>\n', '')
    .replace('  <script src="../../etp-e7-module-host.js"></script>\n', '')
    .replace(/      <section class="card" aria-labelledby="etpE7Heading">[\s\S]*?      <\/section>\r?\n(?=    <\/section>)/, '')
    .replace(', serviceController = null;', ';')
    .replace(/      function serviceScope\(\) \{[\s\S]*?^      \}\r?\n(?=      function setBusy)/m, '')
    .replace("      document.getElementById('etpE7Refresh').addEventListener('click', refreshServiceVerification);\n", '')
    .replace('      refreshServiceVerification();\n', '')
    .replace('      var presentationController = null, operationalController = null, operationalSequence = 0;', '      var presentationController = null;')
    .replace('        refreshOperational();\n', '')
    .replace(/      async function refreshOperational\(\) \{[\s\S]*?^      \}\n/m, '')
    .replace('Select one or more R003, R013, R022 and R025 exports. Monthly exports are combined locally;', 'Select the exact R003, R013, R022 and R025 reports. A workbook may span multiple financial years;')
    .replace('<select data-field="financial-year" data-etp-scope="financialYear" id="etpFinancialYear"><option value="">Select financial year</option></select>', '<input data-field="financial-year" data-etp-scope="financialYear" inputmode="numeric" maxlength="7" placeholder="2026-27" autocomplete="off">')
    .replaceAll(' type="file" multiple accept=', ' type="file" accept=')
    .replaceAll('Choose one or more All Discount Type XLSX exports', 'Choose All Discount Type XLSX')
    .replaceAll('Choose one or more CRO Wise Sales XLSX exports', 'Choose CRO Wise Sales XLSX')
    .replaceAll('Choose one or more Revenue Report XLSX exports', 'Choose Revenue Report XLSX')
    .replaceAll('Choose one or more SDB Variantwise Sales XLSX exports', 'Choose SDB Variantwise Sales XLSX')
    .replace('I confirm the selected exports collectively cover the complete period for all four reports without missing or overlapping date ranges.', 'I confirm all four exports cover the complete selected period.')
    .replace('Select an exact scope and at least one export for each report.', 'Select an exact scope and all four reports.')
    .replace("      var MAX_FILES_PER_REPORT = 13;\n", '')
    .replace(/      function populateFinancialYears\(\) \{[\s\S]*?      function sameScope/, "      function filesReady() { return REPORTS.every(function (id) { return !!state.files[id]; }); }\n      function sameScope")
    .replace('          state.files[id] = [];', '          state.files[id] = null;')
    .replace("          text(document.querySelector('[data-etp-file-name=\"' + id + '\"]'), 'Choose one or more ' + id + ' XLSX exports');", "          text(document.querySelector('[data-etp-file-name=\"' + id + '\"]'), 'Choose ' + id + ' XLSX export');")
    .replace('Select a valid store, financial year and complete date period.', 'Enter a valid store, financial year and complete date period.')
    .replace('Select 1–13 XLSX exports for each of R003, R013, R022 and R025.', 'Select one exact XLSX export for each of R003, R013, R022 and R025.')
    .replace('Confirm that the selected exports collectively cover the complete period.', 'Confirm that all four exports cover the complete selected period.')
    .replace('Combining and validating the selected report exports securely.', 'Validating four reports securely.')
    .replace('files: selectedFiles(),', "files: REPORTS.map(function (id) { return { selectedReportId: id, file: state.files[id] }; }),")
    .replace("          var id = input.getAttribute('data-etp-file'), files = Array.prototype.slice.call(input.files || [], 0, MAX_FILES_PER_REPORT); state.files[id] = files;\n          text(document.querySelector('[data-etp-file-name=\"' + id + '\"]'), files.length ? files.length + ' export' + (files.length === 1 ? '' : 's') + ' selected' : 'Choose one or more ' + id + ' XLSX exports');", "          var id = input.getAttribute('data-etp-file'), file = input.files && input.files[0]; state.files[id] = file || null;\n          text(document.querySelector('[data-etp-file-name=\"' + id + '\"]'), file ? file.name : 'Choose ' + id + ' XLSX export');")
    .replace('      populateFinancialYears();\n', '')
    .replace("    .etp-e2-views{display:flex;gap:7px;flex-wrap:wrap;margin:0 0 12px}.etp-e2-views .action[aria-pressed=\"true\"]{border-color:var(--navy);background:var(--navy);color:#fff}.etp-e2-banner{border-left:5px solid #247a52}.etp-e2-banner h3{color:#1d6946}\n", '')
    .replace(/      <div class="etp-e2-views"[^\n]+\n/, '')
    .replace(/      <div class="placeholder-grid" data-etp-phase6h-operations>[\s\S]*?      <\/div>\n/, '');
}
export function restorePrePhase6h1GatewaySource(source) {
  return String(source)
    .replace("  var REPORTS = Object.freeze(['R003', 'R013', 'R022', 'R025']), MAX_FILES_PER_REPORT = 13, MAX_IMPORT_FILES = REPORTS.length * MAX_FILES_PER_REPORT;", "  var REPORTS = Object.freeze(['R003', 'R013', 'R022', 'R025']);")
    .replace("!Array.isArray(request.files) || request.files.length < REPORTS.length || request.files.length > MAX_IMPORT_FILES", "!Array.isArray(request.files) || request.files.length !== 4")
    .replace('      var counts = Object.create(null), files = [];', '      var seen = Object.create(null), files = [];')
    .replace(`        counts[id] = (counts[id] || 0) + 1;
        if (!exact(item, ['selectedReportId', 'file']) || REPORTS.indexOf(id) < 0 || counts[id] > MAX_FILES_PER_REPORT || !item.file) return failure('ETP_REPORT_SELECTION_INVALID', 'SELECT');
        files.push({ selectedReportId: id, file: item.file });
      }
      if (!REPORTS.every(function (id) { return counts[id] > 0; })) return failure('ETP_REPORT_SELECTION_INVALID', 'SELECT');`, `        if (!exact(item, ['selectedReportId', 'file']) || REPORTS.indexOf(id) < 0 || seen[id] || !item.file) return failure('ETP_REPORT_SELECTION_INVALID', 'SELECT');
        seen[id] = true; files.push({ selectedReportId: id, file: item.file });
      }`)
    .replace('  var ANALYTICS_PAGE_LIMIT = 200;\n  var ANALYTICS_MAX_PAGES = 250;\n', '')
    .replace(/  var ANALYTICS_PROJECTIONS = Object\.freeze\(\{[\s\S]*?\n  \}\);\n/, '')
    .replace(', analyticsApi = options.analyticsApi', '')
    .replace('var seen = Object.create(null), projected = [], allowed = ANALYTICS_PROJECTIONS[reportId];', 'var seen = Object.create(null), projected = [], allowed = PROJECTIONS[reportId];')
    .replace(/\n    async function analyticsRows\([\s\S]*?\n    \/\* Phase-6D baseline was:/, '\n    /* Phase-6D baseline was:')
    .replace("    Object.defineProperty(readFacade, 'loadAnalytics', { value: loadAnalytics, enumerable: false, writable: false, configurable: false });\n", '')
    .replace(', analyticsApi: root && root.SaagarEtpVerifiedAnalytics', '');
}

export function restorePreEtpBatchRuntime(source) {
  return String(source)
    .replace("var REPORTS=['R003','R013','R022','R025'],CHUNK_ROWS=500,CHUNK_FACT_BYTES=480*1024,MAX_FILES_PER_REPORT=13,MAX_IMPORT_FILES=52;", "var REPORTS=['R003','R013','R022','R025'],CHUNK_ROWS=500;")
    .replace(/  function appendFactChunks\([^\n]+\n/, '')
    .replace("if(!value||!Array.isArray(value.files)||value.files.length<REPORTS.length||value.files.length>MAX_IMPORT_FILES)return failure('ETP_FOUR_REPORTS_REQUIRED','PREFLIGHT');", "if(!value||!Array.isArray(value.files)||value.files.length!==4)return failure('ETP_FOUR_REPORTS_REQUIRED','PREFLIGHT');")
    .replace('        var counts={},items=[];', '        var seen={},items=[];')
    .replace(`          counts[selected]=(counts[selected]||0)+1;
          if(REPORTS.indexOf(selected)<0||counts[selected]>MAX_FILES_PER_REPORT||!value.files[i].file)return failure('ETP_REPORT_SELECTION_INVALID','PREFLIGHT');
          var bytes;`, `          if(REPORTS.indexOf(selected)<0||seen[selected]||!value.files[i].file)return failure('ETP_REPORT_SELECTION_INVALID','PREFLIGHT');
          seen[selected]=true;var bytes;`)
    .replace("        if(!REPORTS.every(function(id){return counts[id]>0;}))return failure('ETP_FOUR_REPORTS_REQUIRED','PREFLIGHT');\n", '')
    .replace('        var reports={},sourceHashes={};', '        var reports={};')
    .replace(`          var prior=reports[loaded.reportId];
          if(prior&&(prior.signatureKey!==loaded.signatureKey||prior.profileVersion!==loaded.profileVersion||prior.parserVersion!==loaded.parserVersion||prior.storeCode!==loaded.storeCode))return failure('ETP_REPORT_BATCH_MISMATCH','PARSE',{reportId:loaded.reportId});
          sourceHashes[loaded.reportId]=sourceHashes[loaded.reportId]||[];sourceHashes[loaded.reportId].push(await digest(cryptoApi,item.bytes));
          reports[loaded.reportId]=Object.assign({},loaded,{rows:(prior?prior.rows:[]).concat(loaded.rows)});`, `          loaded.sourceSha256=await digest(cryptoApi,item.bytes);
          loaded.headerSignatureSha256=await digest(cryptoApi,utf8(loaded.signatureKey));
          reports[loaded.reportId]=loaded;`)
    .replace("        for(var r=0;r<REPORTS.length;r++){var id=REPORTS[r],report=reports[id];report.sourceSha256=await digest(cryptoApi,utf8(sourceHashes[id].slice().sort().join('|')));report.headerSignatureSha256=await digest(cryptoApi,utf8(report.signatureKey));}\n", '')
    .replace("          if(!appendFactChunks(chunks,id,factRows))return failure('ETP_FACT_ROW_BYTES_EXCEEDED','VALIDATE',{reportId:id});", "          for(var at=0;at<factRows.length;at+=CHUNK_ROWS)chunks.push({reportId:id,chunkIndex:Math.floor(at/CHUNK_ROWS),rows:factRows.slice(at,at+CHUNK_ROWS)});");
}

export function restorePreEtpBatchWorkerClient(source) {
  return String(source).replace("type:'PARSE_REPORT_BATCH'", "type:'PARSE_FOUR_REPORTS'");
}

export function restorePreEtpBatchWorker(source) {
  return String(source).replace(
    /  self\.onmessage=async function\(event\)\{var value=event&&event\.data;if\(!value\|\|value\.type!==\'PARSE_REPORT_BATCH\'[\s\S]*?\}\}\n(?=\}\)\(\);)/,
    "  self.onmessage=async function(event){var value=event&&event.data;if(!value||value.type!=='PARSE_FOUR_REPORTS'||!Array.isArray(value.items)||value.items.length!==4){self.postMessage({ok:false,code:'ETP_WORKER_REQUEST_INVALID'});return;}try{var loader=SaagarEtpRetailXlsxLoader.create({readWorkbook:function(bytes,settings){return readXlsxFile(bytes,settings);},unzipParts:function(bytes){return fflate.unzipSync(bytes);}}),reports={};for(var i=0;i<value.items.length;i++){var item=value.items[i],loaded=await loader.load({bytes:item.bytes,fileLabel:item.fileLabel,selectedReportId:item.selectedReportId,expectedStoreCode:value.scope.storeCode,datePolicy:value.datePolicy});if(!loaded.ok){self.postMessage({ok:false,code:loaded.code});return;}var sourceSha256=await digest(item.bytes),headerSignatureSha256=await digest(utf8(loaded.signatureKey));reports[loaded.reportId]=Object.freeze(Object.assign({},loaded,{sourceSha256:sourceSha256,headerSignatureSha256:headerSignatureSha256}));}self.postMessage({ok:true,reports:reports});}catch(_){self.postMessage({ok:false,code:'ETP_WORKER_FAILED'});}}\n"
  );
}

export function restorePrePhase6h1PresentationSource(source) {
  return String(source)
    .replace(/  function valueOrDash\([\s\S]*?(?=  function renderVerified\()/, '')
    .replace(",viewButtons=Array.prototype.slice.call(root.querySelectorAll('[data-etp-analytics-view]'))", '')
    .replace("var busy=false,rerun=false,activeView='DAY',filters=", 'var busy=false,rerun=false,filters=')
    .replace('buttons.concat(viewButtons).forEach', 'buttons.forEach')
    .replace(/if\(typeof gateway\.loadAnalytics==='function'\)\{[\s\S]*?\}else if\(typeof gateway\.queryReport==='function'\)/, "if(typeof gateway.queryReport==='function')")
    .replace("loading(content,'Loading verified '+activeView+' analytics…');", "loading(content,'Loading bounded verified projections…');")
    .replace(/viewButtons\.forEach\(function\(button\)\{[\s\S]*?(?=return freeze\(\{ok:true,refresh:refresh,setFilters:setFilters\}\);\})/, '')
    .replace('return freeze({VERSION:3,', 'return freeze({VERSION:2,')
    .replace(',safeAnalytics:safeAnalytics,renderAnalytics:renderAnalytics', '');
}
