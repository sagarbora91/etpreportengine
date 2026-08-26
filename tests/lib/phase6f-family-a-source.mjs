import crypto from 'node:crypto';

export const PHASE6E_FAMILY_A_SHA256 = Object.freeze({
  payroll: 'be31e33114e11d611d9e3ab60ef80ca7717f35a857611f357aba9384ea49e1e4',
  grooming: '1fd55b9a16a9b70614a82e3c300f780b77c9a4da55cde5785587039639840ea2',
  service: '01d903631e3955071a2126775f8235d89b9ec96b300ad5abdbb4d24e51273330'
});

const FOUNDATION = `<link rel="stylesheet" href="../../shared/module-brand-tokens.css">
<link rel="stylesheet" href="../../shared/module-responsive.css">
<link rel="stylesheet" href="../../shared/module-components.css">
<link rel="stylesheet" href="../../shared/module-table.css">
<script src="../../shared/module-ui-runtime.js"></script>
`;
const TOKENS_10PX = `  --navy:#0d2340; --navy-mid:#1a3a5c; --navy-light:#264d7a;
  --gold:#b8922a; --gold-light:#d4a843; --gold-pale:#fdf6e3;
  --cream:#faf8f3; --paper:#ffffff;
  --red:#b91c1c; --red-pale:#fef2f2; --amber:#b45309; --amber-pale:#fffbeb;
  --green:#166534; --green-pale:#f0fdf4; --blue:#1d4ed8; --blue-pale:#eff6ff;
  --gray-50:#fafafa; --gray-100:#f4f4f5; --gray-200:#e4e4e7; --gray-300:#d4d4d8;
  --gray-400:#a1a1aa; --gray-500:#71717a; --gray-600:#52525b; --gray-700:#3f3f46; --gray-800:#27272a;
  --radius:10px; --radius-lg:16px;
  --font-sans:'DM Sans',system-ui,'Segoe UI',Roboto,Arial,sans-serif;
  --font-serif:'DM Serif Display',Georgia,'Times New Roman',serif;
`;
const sha256 = value => crypto.createHash('sha256').update(value).digest('hex');

export function restorePhase6eEtpGatewaySource(source) {
  return source
    .replace('  var REPORT_PAGE_LIMIT = 100;\n', '')
    .replace(/  function filterValue\(rowValue, filter\) \{[\s\S]*?  function summaryUnits\(/, '  function summaryUnits(')
    .replace(/    \/\* One native page in, one sanitized report page out\.[\s\S]*?    async function loadSummary\(/, '    async function loadSummary(')
    .replace(`    /* Phase-6D baseline was: readFacade = freeze({ listScopes: listScopes, inspectScope: inspectScope, loadSummary: loadSummary }) */
    var readFacade = { listScopes: listScopes, inspectScope: inspectScope, loadSummary: loadSummary };
    Object.defineProperty(readFacade, 'queryReport', { value: queryReport, enumerable: false, writable: false, configurable: false });
    readFacade = freeze(readFacade);`, `    var readFacade = freeze({ listScopes: listScopes, inspectScope: inspectScope, loadSummary: loadSummary });`)
    .replace(', REPORT_PAGE_LIMIT: REPORT_PAGE_LIMIT, create: create', ', create: create');
}

export function restorePhase6eEtpPresentationSource(source) {
  const legacyMount = `  function mount(root,gateway,options){if(!root||typeof root.querySelector!=='function'||!gateway||!options||typeof options.getScope!=='function')return fail('ETP_VIEW_MOUNT_INVALID');var content=root.querySelector('[data-etp-verified-content]'),exceptions=root.querySelector('[data-etp-exceptions-content]'),buttons=Array.prototype.slice.call(root.querySelectorAll('[data-etp-verified-refresh]'));if(!content||!exceptions||!buttons.length)return fail('ETP_VIEW_HOOKS_MISSING');var busy=false,rerun=false;function disabled(value){buttons.forEach(function(button){button.disabled=value;});}function loading(host,message){clear(host);host.appendChild(el(host.ownerDocument,'p','lead',message));}function error(host,code){clear(host);host.appendChild(el(host.ownerDocument,'p','etp-v-error','Verified views unavailable: '+code));}async function refresh(){if(busy){rerun=true;return fail('ETP_VIEW_BUSY');}var scope=injectedScope(options);if(!scope){error(content,'ETP_VIEW_SCOPE_REQUIRED');error(exceptions,'ETP_VIEW_SCOPE_REQUIRED');return fail('ETP_VIEW_SCOPE_REQUIRED');}busy=true;disabled(true);loading(content,'Loading bounded verified projections…');loading(exceptions,'Loading reconciliation and quarantine status…');var result;try{result=await load(gateway,scope);}catch(_){result=fail('ETP_VIEW_REFRESH_FAILED');}busy=false;disabled(false);if(rerun){rerun=false;return refresh();}if(!result.ok){error(content,result.code);error(exceptions,result.code);return result;}renderVerified(content,result);renderExceptions(exceptions,result);return result;}buttons.forEach(function(button){button.addEventListener('click',refresh);});return freeze({ok:true,refresh:refresh});}
  return freeze({VERSION:1,REPORTS:REPORTS,PAGE_LIMIT:PAGE_LIMIT,MAX_PAGES:MAX_PAGES,MAX_ROWS:MAX_ROWS,load:load,injectedScope:injectedScope,mount:mount});`;
  return source
    .replace(`/* Privacy-safe, read-only Retail ETP presentation. Each source page is filtered,
   ordered, rendered and discarded independently; no global totals/order claimed. */`, `/* Privacy-safe, read-only Retail ETP presentation. Pages are aggregated and
   discarded; the controller never retains or renders source rows. */`)
    .replace(/  var QUERY_VERSION='ETP_QUERY_V1'[\s\S]*?  \}\);\n  function freeze/, '  function freeze')
    .replace(/  function reportRequest\([\s\S]*?(?=  function injectedScope)/, '')
    .replace(/  function mount\([\s\S]*?  return freeze\(\{VERSION:2[^\n]+/, legacyMount);
}

function stripFoundation(html, moduleId) {
  return html.replace(FOUNDATION, '')
    .replace(`<body data-saagar-ui data-saagar-width="auto" data-saagar-width-resolved="mobile">\n<script id="${moduleId}-phase6f-ui-boot">SaagarUiFoundation.configure(document.body,{mode:'auto'});</script>`, '<body>');
}

function validate(moduleId, restored, validateHash) {
  const actual = sha256(restored);
  if (validateHash && actual !== PHASE6E_FAMILY_A_SHA256[moduleId]) {
    throw new Error(`${moduleId} did not reconstruct to Phase 6E (${Buffer.byteLength(restored)} bytes, ${actual})`);
  }
  return restored;
}

function restorePayroll(html, css) {
  const legacyCss = css
    .replace('   MOBILE / COMPACT RESPONSIVE LAYER  (≤899px)', '   MOBILE RESPONSIVE LAYER  (≤640px)')
    .replace('   Added on top of the existing desktop layout. Wide screens (≥900px)', '   Added on top of the existing desktop layout. Wide screens (>640px)')
    .replace('@media (max-width:899px){', '@media (max-width:640px){')
    .replace('   GM SALARY SHEET — CARD-PER-EMPLOYEE REFLOW (≤899px)', '   GM SALARY SHEET — CARD-PER-EMPLOYEE REFLOW (≤520px)')
    .replace('   899px each row becomes', '   520px each row becomes')
    .replace('   REFLOW (≤899px). Same recipe as the #gm-table block above: below 900px', '   REFLOW (≤520px). Same recipe as the #gm-table block above: below 520px')
    .replaceAll('@media (max-width:899px){', '@media (max-width:520px){')
    .replace('@media(max-width:899px){', '@media(max-width:520px){')
    .replace(/\n\/\* Phase 6F Payroll adoption boundary\.[\s\S]*$/, '');
  return stripFoundation(html, 'payroll')
    .replace('<link id="phase6f-payroll-ui-css" rel="stylesheet" href="payroll-ui.css">\n', '')
    .replace('\n\n<style id="st-v5-hide-css">', `\n\n<style>\n${legacyCss}</style>\n<style id="st-v5-hide-css">`)
    .replace(/ class="table-wrap(?: saagar-table-region--grid)?"(?: data-payroll-table-region="[^"]+")?/g, ' class="table-wrap"')
    .replaceAll(' saagar-table-region--grid', '')
    .replace(/ data-payroll-table-region="[^"]+"/g, '')
    .replace(/ class="saagar-table saagar-table--(?:cards|grid)" data-payroll-table-strategy="[^"]+" data-payroll-table-workflow="[^"]+"(?: data-saagar-grid-reason="[^"]+")?/g, '')
    .replace(' class="stbl saagar-table saagar-table--cards" id="slipTbl" data-payroll-table-strategy="cards" data-payroll-table-workflow="salary-slips"', ' class="stbl" id="slipTbl"');
}

function restoreGrooming(html, css) {
  const legacyCss = css
    .replace(':root{\n', `:root{\n${TOKENS_10PX}`)
    .replace(/\n\/\* API-23 \/ Chrome-44 does not support CSS Grid\.[\s\S]*$/, '');
  return stripFoundation(html, 'grooming')
    .replace('<link id="grooming-phase6f-css" rel="stylesheet" href="grooming-ui.css">', `<style>\n${legacyCss}</style>`)
    .replaceAll(' saagar-table-region--grid', '')
    .replace(/ data-grooming-table-region="[^"]+"/g, '')
    .replace(' class="mt saagar-table saagar-table--grid"', ' class="mt"')
    .replace(/ data-grooming-table-strategy="[^"]+" data-grooming-table-workflow="[^"]+" data-saagar-grid-reason="[^"]+"/g, '');
}

function restoreService(html, css) {
  css = css
    .replace('@media (max-width: 899px) { .fu-addform { grid-template-columns: 1fr; } }', '@media (max-width: 640px) { .fu-addform { grid-template-columns: 1fr; } }')
    .replace('/* ═══════════ MOBILE / COMPACT (≤899px) ═══════════ */', '/* ═══════════════ MOBILE (≤640px) ═══════════════ */')
    .replace('@media (max-width: 899px) {', '@media (max-width: 640px) {')
    .replace('@media(max-width:899px){.d3-workboard-host{padding:12px}.d3-head{display:block}.d3-context{margin-top:8px}.d3-board{grid-template-columns:1fr;overflow-x:hidden}.d3-lane{min-width:0}.d3-ex-list{grid-template-columns:1fr}.stage-chips{flex-wrap:wrap;overflow-x:hidden;padding-bottom:4px}.stage-chip{flex:1 1 auto}}', '@media(max-width:720px){.d3-workboard-host{padding:12px}.d3-head{display:block}.d3-context{margin-top:8px}.d3-ex-list{grid-template-columns:1fr}.stage-chips{flex-wrap:nowrap;overflow-x:auto;-webkit-overflow-scrolling:touch;padding-bottom:4px}.stage-chip{flex:0 0 auto}}')
    .replace('@media (max-width: 899px) {', '@media (max-width: 640px) {');
  const [delta, , ...mainParts] = css.split('\n');
  const main = mainParts.join('\n')
    .replace(':root{\n', `:root{\n${TOKENS_10PX}\n`)
    .replace(/\n\/\* Phase 6F: table overflow is opt-in[\s\S]*$/, '');
  return stripFoundation(html, 'service')
    .replace('<link id="st-v5-mobile-css" rel="stylesheet" href="../../shared/module-mobile-legacy.css"><script id="st-v5-mobile-boot">', `<link id="st-v5-mobile-css" rel="stylesheet" href="../../shared/module-mobile-legacy.css"><style id="st-v5-mobile-css-delta">\n${delta}\n</style><script id="st-v5-mobile-boot">`)
    .replace('<link id="phase6f-service-ui-css" rel="stylesheet" href="service-ui.css">', `<style>\n${main}\n</style>`)
    .replace(' class="tbl-scroll saagar-table-region--cards" data-service-table-region="condition-report"', ' class="tbl-scroll"')
    .replace(' class="cond-table saagar-table saagar-table--cards" data-service-table-strategy="cards" data-service-table-workflow="condition-report"', ' class="cond-table"')
    .replace(' class="tbl-scroll saagar-table-region--grid" data-service-table-region="service-estimate"', ' class="tbl-scroll"')
    .replace(' class="est-table saagar-table saagar-table--grid" data-service-table-strategy="grid" data-service-table-workflow="service-estimate" data-saagar-grid-reason="quantity unit price and authorized total require cross-column comparison"', ' class="est-table"');
}

export function restorePhase6eFamilyASource(moduleId, html, css, validateHash = true) {
  const restore = { payroll: restorePayroll, grooming: restoreGrooming, service: restoreService }[moduleId];
  if (!restore) throw new Error(`Unsupported Phase 6F Family-A module: ${moduleId}`);
  const common = '<link rel="stylesheet" href="../../shared/module-mobile-common.css">\n';
  const legacy = '<link id="st-v5-mobile-css" rel="stylesheet" href="../../shared/module-mobile-legacy.css">';
  return validate(moduleId, restore(html.includes(legacy) ? html : html.replace(common, common + legacy), css), validateHash);
}
