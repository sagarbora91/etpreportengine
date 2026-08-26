import crypto from 'node:crypto';

export const PRE_PHASE6G_FAMILY_B_SHA256 = Object.freeze({
  expense: 'c55bbcafc7b38b935145cdaf6e618e8991c96f578352644789693c8edf7fc334',
  leave: 'f4fb54c9e300878f75186db74925d63f2adf31078c4b8385a0808ccded39a744',
  cro_audit: 'f92861c06c46befda9e97b0511424cbf01b804c7fbe25dead0ad0b06767c0355',
  tax: 'faf4b01df0b460791d349093a294e173e5f1007c5739c092f9f5eef87bb9fbcd',
  dsr: 'a24a0b414552e3cef6891c787cabf44d57f3e6d219480e9d7ed749ce9183dc3a'
  ,qms: '6416be67e9d5589dd491b6d2c500459a8d5fd5cd25b87304d558318d53e49166'
});

const FOUNDATION = `<link rel="stylesheet" href="../../shared/module-responsive.css">
<link rel="stylesheet" href="../../shared/module-components.css">
<link rel="stylesheet" href="../../shared/module-table.css">
<script src="../../shared/module-ui-runtime.js"></script>
`;

const sha256 = value => crypto.createHash('sha256').update(value).digest('hex');

function stripPolicy(source, id) {
  return source
    .replace(FOUNDATION, '')
    .replace('<script src="../../shared/module-table-runtime.js"></script>\n', '')
    .replace('<script src="../../shared/module-rendered-components.js"></script>\n', '')
    .replace(new RegExp(`<script id="phase6g-${id}-render-policy">[\\s\\S]*?<\\/script>\\n`), '');
}

function restoreExtractedCss(source, moduleId, css) {
  return source.replace(
    new RegExp(`<link id="phase6g-${moduleId.replace('_', '-')}-ui-css" rel="stylesheet" href="[^"]+">`),
    `<style>\n${css}</style>`
  );
}

function restoreExpense(source) {
  source = source
    .replace('/* ────── MOBILE / COMPACT RESPONSIVE LAYER (≤899px) ──────', '/* ───────── MOBILE RESPONSIVE LAYER (phones ≤640px) ─────────')
    .replace('@media(max-width:899px){', '@media(max-width:640px){')
    .replace(`
  /* Keep every destination visible without a sideways tab gesture. */
  .tabs{flex-wrap:wrap;overflow-x:hidden;padding:4px 8px}
  .tab{flex:1 1 auto;text-align:center;padding:10px 12px}
`, '')
    .replace('  /* ─────── LIST/RECORD TABLES → CARDS (compact ≤899px) ───────', '  /* ───────── LIST/RECORD TABLES → CARDS (phones ≤640px) ─────────');
  let restored = stripPolicy(source, 'expense')
    .replace('<link rel="stylesheet" href="../../shared/module-brand-tokens.css">\n', '')
    .replace('<body data-saagar-ui data-saagar-width="auto">\n<script id="phase6g-expense-ui-boot">SaagarUiFoundation.configure(document.body,{mode:\'auto\'});SaagarRenderedComponents.observe(document.body,ExpenseRenderedPolicy);</script>', '<body>')
    .replace(/\nhtml\.saagar-legacy-webview \.kgrid,[\s\S]*?flex:1 1 280px\}\n(?=<\/style>)/, '')
    .replace(/ data-saagar-(?:print-)?table-strategy="[^"]+"/g, '')
    .replace(/ data-saagar-table-workflow="[^"]+"/g, '')
    .replace(/ data-saagar-grid-reason="[^"]+"/g, '')
    .replace("function esc(s){ return String(s==null?'':s).replace(/[&<>\"']/g,function(c){return{'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;',\"'\":'&#39;'}[c];}); }\nfunction badgeToken(s){ var v=String(s==null?'':s).toLowerCase(); return /^(cash|bank|upi|card|auto)$/.test(v)?v:'auto'; }\n/* JavaScript-source encoder for a value placed inside a single-quoted inline\n   handler argument. This is deliberately separate from HTML text escaping. */\nfunction jsq(s){ if(s!=null&&typeof s!=='string'&&typeof s!=='number'&&typeof s!=='boolean')throw new TypeError('inline argument must be primitive'); var v=String(s==null?'':s); if(v.length>512)throw new RangeError('inline argument too long'); return v.replace(/[\\\\'\"&<>\\u0000-\\u001f\\u007f\\u2028\\u2029]/g,function(c){var n=c.charCodeAt(0),h=n.toString(16);while(h.length<(n<=255?2:4))h='0'+h;return n<=255?'\\\\x'+h:'\\\\u'+h;}); }\nfunction jargs(v){ if(v!=null&&typeof v!=='string'&&typeof v!=='number'&&typeof v!=='boolean')throw new TypeError('delegated argument must be primitive'); if(typeof v==='string'&&v.length>512)throw new RangeError('delegated argument too long'); return encodeURIComponent(JSON.stringify([v])); }", "function esc(s){ return String(s==null?'':s).replace(/[&<>\"]/g,function(c){return{'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;'}[c];}); }")
    .replaceAll('+esc(e.source)+', '+e.source+')
    .replaceAll("badgeToken(i.mode)+'\">'+esc(i.mode)", "i.mode.toLowerCase()+'\">'+i.mode")
    .replace("jsq(c)+'\\',this.value)", "c+'\\',this.value)")
    .replace(/\nvar ExpenseDelegatedHandlers=[\s\S]*?SaagarRenderedComponents\.connect\(document\.body,ExpenseRenderedPolicy,ExpenseDelegatedHandlers\);\n/, '\n');
  const delegated = {
    'view-ledger-photo':['viewPhoto','e.id'],'share-ledger-entry':['shareEntry','e.id'],
    'approve-ledger-entry':['approveEntry','e.id'],'edit-ledger-entry':['editEntry','e.id'],'void-ledger-entry':['voidEntry','e.id'],
    'post-recurring-entry':['postRecurring','t.id'],'edit-recurring-template':['editRecTpl','t.id'],
    'toggle-recurring-template':['toggleRecTpl','t.id'],'delete-recurring-template':['delRecTpl','t.id'],
    'filter-statement-all-stores':['setSST',"''"],'filter-statement-store':['setSST','esc(s.code)'],
    'approve-store-day':['approveStoreDay','esc(store)'],'reopen-store-day':['reopenStoreDay','esc(store)'],
    'close-store-day':['closeStoreDay','esc(store)'],'post-source-items':['postSrc','src'],
    'post-all-source-items':['postSrc',"'all'"],'pay-receivable':['payReceivable','r.id'],
    'remind-receivable':['remindReceivable','r.id'],'void-receivable':['voidReceivable','r.id']
  };
  for (const [action, [handler, arg]] of Object.entries(delegated)) {
    restored = restored.replaceAll(
      `data-action="${action}" data-saagar-args="'+jargs(${arg === 'esc(s.code)' ? 's.code' : arg === 'esc(store)' ? 'store' : arg})+'"`,
      `data-action="${action}"`
    );
    const marker = `data-action="${action}"`;
    restored = restored.replaceAll(new RegExp(`${marker}([^>]*)(?=>)`, 'g'), (_m, attrs) => `${marker}${attrs} onclick="${handler}(\\''+${arg}+'\\')"`);
  }
  return restored
    .replace("onclick=\"setSST(\\''+''+'\\')\"", "onclick=\"setSST(\\'\\')\"")
    .replace("onclick=\"postSrc(\\''+'all'+'\\')\"", "onclick=\"postSrc(\\'all\\')\"")
    .replace('</style>\n<style id="st-v5-hide-css">', '\n</style>\n<style id="st-v5-hide-css">');
}

function restoreLeave(source, css) {
  css = css
    .replace('/* ── MOBILE / COMPACT LAYER. Desktop screens (≥900px) unchanged. ── */', '/* ── MOBILE LAYER (phones ~360px). Wide screens (>640px) unchanged. ── */')
    .replace('@media (max-width: 899px) {', '@media (max-width: 640px) {')
    .replace(`    overflow-x: hidden;
    flex-wrap: wrap;`, `    overflow-x: auto;            /* horizontal scroll instead of squashing */
    -webkit-overflow-scrolling: touch;
    flex-wrap: nowrap;`)
    .replace('  .header-actions .btn { flex: 1 1 auto; justify-content: center; }', '  .header-actions .btn { flex: 0 0 auto; }')
    .replace(`
  /* Seven staffing days wrap in-place; no hidden sixth/seventh card. */
  .staff-strip { flex-wrap: wrap; overflow-x: hidden; }
  .staff-day { min-width: 0; flex: 1 1 calc(25% - 8px); }
`, '');
  const legacyCss = css.replace(/\n\/\* Phase 6G: the monthly employee report[\s\S]*$/, '');
  let restored = restoreExtractedCss(stripPolicy(source, 'leave'), 'leave', legacyCss)
    .replace('<body data-saagar-ui data-saagar-width="auto" data-saagar-width-resolved="mobile">\n<script id="leave-phase6g-ui-boot">SaagarUiFoundation.configure(document.body,{mode:\'auto\'});SaagarRenderedComponents.observe(document.body,LeaveRenderedPolicy);</script>', '<body>')
    .replace("  const wrap = document.createElement('div'); wrap.className = 'saagar-table-region--grid';\n  const tbl  = document.createElement('table');\n  tbl.className = 'report-table saagar-table saagar-table--grid';\n  tbl.dataset.saagarTableWorkflow = 'monthly-leave-report';\n  tbl.dataset.saagarTableStrategy = 'grid';\n  tbl.dataset.saagarGridReason = 'employee leave categories balances and totals require cross-column comparison';", "  const wrap = document.createElement('div'); wrap.style.overflowX = 'auto';\n  const tbl  = document.createElement('table'); tbl.className = 'report-table';")
    .replace(/\nfunction approveLeaveRecord[\s\S]*?SaagarRenderedComponents\.connect\(document\.body, LeaveRenderedPolicy, LeaveDelegatedHandlers\);\n/, '\n');
  return restored
    .replace('onclick="openImportPicker()"', 'onclick="document.getElementById(\'importInput\').click()"')
    .replace('onclick="openDayLeaveFromControl()"', 'onclick="openAddLeaveModal(currentDayKey)"')
    .replace('onclick="openDayAgendaFromControl()"', 'onclick="openAddAgendaModal(currentDayKey)"')
    .replace('onclick="openEmployeeMasterFromLeave()"', 'onclick="closeAddLeave();openEmployeeMaster();"')
    .replace(/(const ok = document\.createElement\('button'\); ok\.dataset\.action = 'approve-pending-leave';) ok\.dataset\.saagarArgs = [^;]+;/, "$1 ok.className = 'btn btn-green'; ok.textContent = '✓ Approve';\n      ok.onclick = () => decideLeave(p.key, l.id, 'approved');\n      const no = document.createElement('button'); no.dataset.action = 'reject-pending-leave'; no.className = 'btn btn-danger'; no.textContent = '⊘ Reject';\n      no.onclick = () => decideLeave(p.key, l.id, 'rejected');")
    .replace(/\n      const no = document\.createElement\('button'\); no\.dataset\.action = 'reject-pending-leave';[^\n]+/, '')
    .replace(/(del\.dataset\.action = 'remove-blackout';) del\.dataset\.saagarArgs = [^;]+;/, "$1 del.className = 'emp-del'; del.title = 'Remove'; del.textContent = '×';\n    del.onclick = () => removeBlackout(e.id);")
    .replace(" del.className = 'emp-del'; del.title = 'Remove'; del.textContent = '×';", '')
    .replace(/(ok\.dataset\.action = 'approve-day-leave';) ok\.dataset\.saagarArgs = [^;]+;/, "$1 ok.className = 'leave-row-del';")
    .replace(" ok.className = 'leave-row-del';", '')
    .replace("        ok.title = 'Approve'; ok.textContent = '✓';", "        ok.title = 'Approve'; ok.textContent = '✓';\n        ok.onclick = e => { e.stopPropagation(); decideLeave(key, l.id, 'approved'); };")
    .replace(/(no\.dataset\.action = 'reject-day-leave';) no\.dataset\.saagarArgs = [^;]+;/, "$1 no.className = 'leave-row-del'; no.title = 'Reject'; no.textContent = '⊘';\n        no.onclick = e => { e.stopPropagation(); decideLeave(key, l.id, 'rejected'); };")
    .replace(" no.className = 'leave-row-del'; no.title = 'Reject'; no.textContent = '⊘';", '')
    .replace(/(del\.dataset\.action = 'remove-day-leave';) del\.dataset\.saagarArgs = [^;]+;/, "$1 del.className = 'leave-row-del'; del.title = 'Remove leave'; del.textContent = '×';\n        del.onclick = e => { e.stopPropagation(); removeLeave(key, i); };")
    .replace(" del.className = 'leave-row-del'; del.title = 'Remove leave'; del.textContent = '×';", '')
    .replace(/(del\.dataset\.action = 'remove-day-agenda';) del\.dataset\.saagarArgs = [^;]+;/, "$1 del.className = 'agenda-del'; del.textContent = '✕'; del.title = 'Remove';\n        del.onclick = () => removeAgenda(key, i);")
    .replace(" del.className = 'agenda-del'; del.textContent = '✕'; del.title = 'Remove';", '')
    .replace(/(no\.dataset\.action = 'reject-staff-leave';) no\.dataset\.saagarArgs = [^;]+;/, "$1 no.className = 'btn btn-danger'; no.textContent = '⊘ Reject';\n    no.onclick = () => decideLeave(key, leave.id, 'rejected');")
    .replace(" no.className = 'btn btn-danger'; no.textContent = '⊘ Reject';", '')
    .replace(/(ok\.dataset\.action = 'approve-staff-leave';) ok\.dataset\.saagarArgs = [^;]+;/, "$1 ok.className = 'btn btn-green'; ok.textContent = '✓ Approve';\n    ok.onclick = () => decideLeave(key, leave.id, 'approved');")
    .replace(" ok.className = 'btn btn-green'; ok.textContent = '✓ Approve';", '')
    .replace(/(cog\.dataset\.action = 'edit-employee-entitlements';) cog\.dataset\.saagarArgs = [^;]+;/, "$1 cog.className = 'emp-del';")
    .replace(" cog.className = 'emp-del';", '')
    .replace("    cog.title = 'Set leave entitlements'; cog.textContent = '⚙';", "    cog.title = 'Set leave entitlements'; cog.textContent = '⚙';\n    cog.onclick = () => toggleEntEditor(i);")
    .replace(/(del\.dataset\.action = 'remove-employee';) del\.dataset\.saagarArgs = [^;]+;/, "$1 del.className = 'emp-del'; del.title = 'Remove'; del.textContent = '×';\n    del.onclick = () => removeEmployee(i);")
    .replace(" del.className = 'emp-del'; del.title = 'Remove'; del.textContent = '×';", '')
    .replace(/(sv\.dataset\.action = 'save-employee-entitlements';) sv\.dataset\.saagarArgs = [^;]+;/, "$1 sv.className = 'btn btn-gold'; sv.textContent = 'Save';\n      sv.onclick = () => saveEntOverride(i);")
    .replace(" sv.className = 'btn btn-gold'; sv.textContent = 'Save';", '')
    .replace("const ok = document.createElement('button'); ok.dataset.action = 'approve-pending-leave';\n      ok.onclick = () => decideLeave(p.key, l.id, 'approved');\n      no.onclick = () => decideLeave(p.key, l.id, 'rejected'); ok.className = 'btn btn-green'; ok.textContent = '✓ Approve';\n      const no = document.createElement('button'); no.dataset.action = 'reject-pending-leave'; no.dataset.saagarArgs = SaagarRenderedComponents.encodeArgs([p.key, l.id]);", "const ok = document.createElement('button'); ok.dataset.action = 'approve-pending-leave'; ok.className = 'btn btn-green'; ok.textContent = '✓ Approve';\n      ok.onclick = () => decideLeave(p.key, l.id, 'approved');\n      const no = document.createElement('button'); no.dataset.action = 'reject-pending-leave'; no.className = 'btn btn-danger'; no.textContent = '⊘ Reject';\n      no.onclick = () => decideLeave(p.key, l.id, 'rejected');")
    .replace("del.dataset.action = 'remove-blackout';\n    del.onclick", "del.dataset.action = 'remove-blackout'; del.className = 'emp-del'; del.title = 'Remove'; del.textContent = '×';\n    del.onclick")
    .replace("no.dataset.action = 'reject-day-leave';\n        no.onclick = e => { e.stopPropagation(); decideLeave(key, l.id, 'rejected'); }; no.className = 'leave-row-del'; no.title = 'Reject'; no.textContent = '⊘';", "no.dataset.action = 'reject-day-leave'; no.className = 'leave-row-del'; no.title = 'Reject'; no.textContent = '⊘';\n        no.onclick = e => { e.stopPropagation(); decideLeave(key, l.id, 'rejected'); };")
    .replace("del.dataset.action = 'remove-day-leave';\n        del.onclick = e => { e.stopPropagation(); removeLeave(key, i); }; del.className = 'leave-row-del'; del.title = 'Remove leave'; del.textContent = '×';", "del.dataset.action = 'remove-day-leave'; del.className = 'leave-row-del'; del.title = 'Remove leave'; del.textContent = '×';\n        del.onclick = e => { e.stopPropagation(); removeLeave(key, i); };")
    .replace("del.dataset.action = 'remove-day-agenda';\n        del.onclick = () => removeAgenda(key, i); del.className = 'agenda-del'; del.textContent = '✕'; del.title = 'Remove';", "del.dataset.action = 'remove-day-agenda'; del.className = 'agenda-del'; del.textContent = '✕'; del.title = 'Remove';\n        del.onclick = () => removeAgenda(key, i);")
    .replace("no.onclick = () => decideLeave(key, leave.id, 'rejected'); no.className = 'btn btn-danger'; no.textContent = '⊘ Reject';", "no.onclick = () => decideLeave(key, leave.id, 'rejected');")
    .replace("ok.onclick = () => decideLeave(key, leave.id, 'approved'); ok.className = 'btn btn-green'; ok.textContent = '✓ Approve';", "ok.onclick = () => decideLeave(key, leave.id, 'approved');")
    .replace("del.onclick = () => removeEmployee(i); del.className = 'emp-del'; del.title = 'Remove'; del.textContent = '×';", "del.onclick = () => removeEmployee(i);")
    .replace("sv.dataset.action = 'save-employee-entitlements';\n      sv.onclick = () => saveEntOverride(i); sv.className = 'btn btn-gold'; sv.textContent = 'Save';", "sv.dataset.action = 'save-employee-entitlements'; sv.className = 'btn btn-gold'; sv.textContent = 'Save';\n      sv.onclick = () => saveEntOverride(i);")
    .replace("// Default the viewed month to __stAsOf (falls back to today inside getAsOf()).\n}\napplyAsOfDate();", "// Default the viewed month to __stAsOf (falls back to today inside getAsOf()).\napplyAsOfDate();");
}

function restoreCroAudit(source, css) {
  const legacyCss = css.replace(/\n\/\* Phase 6G \/ API 23:[\s\S]*?(?=\/\* very narrow phones)/, '\n');
  return restoreExtractedCss(stripPolicy(source, 'cro-audit'), 'cro_audit', legacyCss)
    .replace('<body data-saagar-ui data-saagar-width="auto" data-saagar-width-resolved="mobile">\n<script id="phase6g-cro-audit-ui-boot">SaagarUiFoundation.configure(document.body,{mode:\'auto\'});SaagarRenderedComponents.observe(document.body,CroAuditRenderedPolicy);</script>', '<body>')
    .replace(' data-saagar-args="%5B-1%5D" class="pn-btn"', ' class="pn-btn" onclick="shiftPeriod(-1)"')
    .replace(' data-saagar-args="%5B1%5D" class="pn-btn"', ' class="pn-btn" onclick="shiftPeriod(1)"')
    .replace(' data-saagar-args="${croArgs(r.cid)}" type="button" class="btn xs rev-ask"', " type=\"button\" class=\"btn xs rev-ask\" onclick=\"croReviewSend('${stEsc(r.cid)}')\"")
    .replace(' data-saagar-args="${croArgs(a.id)}" class="print-btn">✎', " class=\"print-btn\" onclick=\"loadAuditForEdit('${a.id}')\">✎")
    .replace(' data-saagar-args="${croArgs(a.id)}" class="print-btn">🖨', " class=\"print-btn\" onclick=\"printAudit('${a.id}')\">🖨")
    .replace(" onclick=\"if(event.target.closest('button'))return;showModal(", ' onclick="showModal(')
    .replace(' data-saagar-args="${croArgs(a.id)}" class="hi-del"', " class=\"hi-del\" onclick=\"delAudit('${a.id}',event)\"")
    .replace(/\nfunction croDeleteAudit\([\s\S]*?\n\}/, '')
    .replace(/\nfunction croArgs\([^\n]+/, '')
    .replace(/ data-saagar-table-strategy="grid" data-saagar-table-workflow="print-comparison" data-saagar-grid-reason="[^"]+"/, '')
    .replace(/\nvar CroAuditDelegatedHandlers=[\s\S]*?SaagarRenderedComponents\.connect\(document\.body,CroAuditRenderedPolicy,CroAuditDelegatedHandlers\);\n\n/, '\n');
}

function restoreTax(source, css) {
  css = css
    .replace('@media(max-width:899px){', '@media(max-width:600px){')
    .replace('  .mnav{padding:4px 8px;flex-wrap:wrap;overflow-x:hidden;}', '  .mnav{padding:0 8px;}')
    .replace('  .mt{padding:9px 10px;font-size:10.5px;flex:1 1 25%;text-align:center;}', '  .mt{padding:9px 10px;font-size:10.5px;}')
    .replace('   MOBILE / COMPACT RESPONSIVE LAYER (≤899px)', '   MOBILE RESPONSIVE LAYER (phones ≤640px / ~360px)')
    .replace('   Additive only. Desktop screens (≥900px) are byte-unchanged.', '   Additive only. Wide screens (>640px) are byte-unchanged.')
    .replaceAll('@media(max-width:899px){', '@media(max-width:640px){');
  let legacyCss = css.replace(/\.ac-row-action\{[^\n]+\}\n\n\/\* Phase 6G:[\s\S]*$/, '');
  const split = legacyCss.indexOf('\n\n.ac-badge{');
  const first = legacyCss.slice(0, split);
  const second = legacyCss.slice(split + 2);
  let restored = stripPolicy(source, 'tax')
    .replace('<link rel="stylesheet" href="../../shared/module-brand-tokens.css">\n', '')
    .replace('<link id="phase6g-tax-ui-css" rel="stylesheet" href="tax-ui.css">', `<style>\n${first}\n</style>`)
    .replace('<body data-saagar-ui data-saagar-width="auto" data-saagar-width-resolved="mobile">\n<script id="phase6g-tax-ui-boot">SaagarUiFoundation.configure(document.body,{mode:\'auto\'});</script>', '<body>')
    .replace('<table class="stp-table saagar-table saagar-table--grid" data-saagar-table-strategy="grid" data-saagar-table-workflow="compliance-print" data-saagar-grid-reason="six compliance fields require cross-column comparison">', '<table class="stp-table">')
    .replace(/\/\* Phase 6G rendered-DOM contract\.[\s\S]*?SaagarRenderedComponents\.connect\(document\.body,TaxRenderedPolicy,TaxDelegatedHandlers\);\n\n/, '')
    .replace('data-saagar-args="${taxArgs([f.id])}" data-action-key="definition-01" class="btn-firm-edit">', 'data-action-key="definition-01" class="btn-firm-edit" onclick="editFirm(\'${f.id}\')">')
    .replace('data-saagar-args="${taxArgs([f.id])}" data-action-key="definition-01" class="btn-firm-del">', 'data-action-key="definition-01" class="btn-firm-del" onclick="deleteFirm(\'${f.id}\')">')
    .replace(/data-saagar-args="\$\{taxArgs\(\[c\.id\]\)\}" data-action-key="definition-01" class="btn-master (edit|del|arch)">/g, (_m, cls) => `data-action-key="definition-01" class="btn-master ${cls}" onclick="${cls === 'edit' ? 'editCustomObligation' : cls === 'del' ? 'deleteCustomObligation' : 'toggleArchive'}(\${attrJsStr(c.id)})">`)
    .replace('data-saagar-args="${taxArgs([o.key])}" data-action-key="definition-02" class="btn-master arch">', 'data-action-key="definition-02" class="btn-master arch" onclick="toggleArchive(${attrJsStr(o.key)})">')
    .replace('data-saagar-args="${taxArgs([f.id])}" data-action-key="definition-01" class="ev-view">', 'data-action-key="definition-01" class="ev-view" onclick="evView(${f.id})">')
    .replace('data-saagar-args="${taxArgs([f.id,id])}" data-action-key="definition-01" class="ev-del">', 'data-action-key="definition-01" class="ev-del" onclick="evDelete(${f.id},\'${id}\')">')
    .replace(/<div class="ac-row \$\{isOver\?'is-overdue':'is-soon'\}"><button[^>]+>/, `<div class="ac-row \${isOver?'is-overdue':'is-soon'}" onclick="acOpen('\${r.firmId}','\${r.month}','\${r.itemId||''}')" style="cursor:pointer">`)
    .replace('    </button></div>`;', '    </div>`;')
    .replace(/data-action="show-compliance-month" data-saagar-args="\$\{taxArgs\(\[x\.month\]\)\}"/, 'data-action="showview"')
    .replace(/data-action="show-compliance-month" data-saagar-args="\$\{taxArgs\(\[item\.month\]\)\}"/, 'data-action="showview"')
    .replace(/data-action="completion-toggle" data-saagar-args="\$\{taxArgs\(\[item\.id\]\)\}"/, 'data-action="completion-toggle"')
    .replace(/data-action="donebuttonclass" data-saagar-args="\$\{taxArgs\(\[item\.id\]\)\}"/, 'data-action="donebuttonclass"')
    .replace(/data-action="togglena" data-saagar-args="\$\{taxArgs\(\[item\.id\]\)\}"/g, 'data-action="togglena"')
    .replace(/data-action="togglenote" data-saagar-args="\$\{taxArgs\(\[item\.id\]\)\}"/, 'data-action="togglenote"')
    .replace(/data-action="confirmdone" data-saagar-args="\$\{taxArgs\(\[item\.id\]\)\}"/, 'data-action="confirmdone"')
    .replace(/data-action="canceldone" data-saagar-args="\$\{taxArgs\(\[item\.id\]\)\}"/, 'data-action="canceldone"');
  restored = restored
    .replace('data-action="showview" data-action-key="definition-01" class="exp-btn" style=', 'data-action="showview" data-action-key="definition-01" class="exp-btn" style=')
    .replace('title="${nm} · ${escapeHtml(x.month)}">', 'onclick="showView(\'calendar\');setMonth(\'${x.month}\')" title="${nm} · ${escapeHtml(x.month)}">')
    .replace('    const doneButtonClass=`done-btn${isDone?\' done\':\'\'}`;\n    const doneButton=isNA', "    const doneButtonClass=`done-btn${isDone?' done':''}`;\n    const doneButtonAction=`clickDone('${item.id}')`;\n    const doneButton=isNA")
    .replace('class="${doneButtonClass}" title="Not Applicable', 'class="${doneButtonClass}" onclick="${doneButtonAction}" title="Not Applicable')
    .replace(/(data-action="(?:completion-toggle|donebuttonclass)" data-action-key="definition-0[12]" class="\$\{doneButtonClass\}")/g, '$1 onclick="${doneButtonAction}"')
    .replace(/(data-action="togglena" data-action-key="definition-0[12]" class="na-toggle(?: on)?")/g, '$1 onclick="toggleNA(\'${item.id}\')"')
    .replace('data-action="togglenote" data-action-key="definition-01" class="exp-btn"', 'data-action="togglenote" data-action-key="definition-01" class="exp-btn" onclick="toggleNote(\'${item.id}\')"')
    .replace('data-action="confirmdone" data-action-key="definition-01" class="btn-confirm"', 'data-action="confirmdone" data-action-key="definition-01" class="btn-confirm" onclick="confirmDone(\'${item.id}\')"')
    .replace('data-action="canceldone" data-action-key="definition-01" class="btn-cancel-done"', 'data-action="canceldone" data-action-key="definition-01" class="btn-cancel-done" onclick="cancelDone(\'${item.id}\')"')
    .replace('data-action="showview" data-action-key="definition-02" class="exp-btn">Open →', 'data-action="showview" data-action-key="definition-02" class="exp-btn" onclick="showView(\'calendar\');setMonth(\'${item.month}\')">Open →');
  const scriptMarker = '<script>\n/* ══════════════════════════════════════════════════════';
  restored = restored.replace(scriptMarker, `<style>\n${second}</style>\n\n${scriptMarker}`)
    .replace('</div>\n\n<style>\n.ac-badge', '</div>\n<style>\n.ac-badge');
  return restored;
}

function restoreDsr(source, css) {
  const legacyCss = css.replace(/\n\/\* Phase 6H\.1: read-only, sanitized ETP MTD panel\. \*\/[\s\S]*$/, '')
    .replace(/\n\n\/\* Phase 6G: only reviewed comparison tables[\s\S]*?html\.saagar-legacy-webview \.sgrid > \* \{[^\n]+\}\n/, '\n');
  source = source
    .replace('<script src="../../etp-analytics-consumer.js"></script>\n', '')
    .replace(/    <section class="dsr-etp-e2"[\s\S]*?    <\/section>\n/, '')
    .replace('  renderDsrEtpAnalytics();\n', '')
    .replace(/var dsrEtpAnalyticsSeq=0;\nasync function renderDsrEtpAnalytics\(\)\{[\s\S]*?\n\}\n/, '');
  return restoreExtractedCss(stripPolicy(source, 'dsr'), 'dsr', legacyCss)
    .replace('<body data-saagar-ui data-saagar-width="auto" data-saagar-width-resolved="mobile">\n<script id="dsr-phase6g-ui-boot">SaagarUiFoundation.configure(document.body,{mode:\'auto\'});SaagarRenderedComponents.observe(document.body,DsrRenderedPolicy);</script>', '<body>')
    .replace(/ class="var-tbl saagar-table saagar-table--grid"([^>]*) data-saagar-table-strategy="grid" data-saagar-table-workflow="stock-variance" data-saagar-grid-reason="[^"]+"/, ' class="var-tbl"$1')
    .replace('<div class="saagar-table-region--cards" aria-label="Staff performance rollup"><table class="ptbl saagar-table saagar-table--cards" aria-label="Staff performance" data-saagar-table-strategy="cards" data-saagar-table-workflow="staff-rollup">', '<table class="ptbl">')
    .replace('</table></div>`;', '</table>`;')
    .replace(/\nfunction openDsrFollowUp\([\s\S]*?\n\}/, '')
    .replace("data-saagar-args=\"'+SaagarRenderedComponents.encodeArgs(['https://wa.me/91'+f.m10+'?text='+enc])+'\" data-action-key=\"definition-01\" class=\"btn btn-green btn-sm\">", "data-action-key=\"definition-01\" class=\"btn btn-green btn-sm\" onclick=\"stWaLink(\\'https://wa.me/91'+esc(f.m10)+'?text='+enc+'\\',\\'dsr-nonbuyer-followup\\',\\'promotional-customer-message\\')\">")
    .replace("data-saagar-args=\"'+SaagarRenderedComponents.encodeArgs([f.date,f.ref])+'\" data-action-key=\"definition-01\" class=\"btn btn-primary btn-sm\">", "data-action-key=\"definition-01\" class=\"btn btn-primary btn-sm\" onclick=\"markFollowedUp(\\''+esc(f.date)+'\\',\\''+esc(f.ref)+'\\')\">")
    .replace('data-saagar-args="${SaagarRenderedComponents.encodeArgs([sref])}" data-action-key="definition-01" class="btn btn-green btn-sm" style="flex:1">', 'data-action-key="definition-01" class="btn btn-green btn-sm" style="flex:1" onclick="visitorPurchase(\'${sref}\')">')
    .replace('data-saagar-args="${SaagarRenderedComponents.encodeArgs([sref])}" data-action-key="definition-01" class="btn btn-outline btn-sm" style="flex:1">', 'data-action-key="definition-01" class="btn btn-outline btn-sm" style="flex:1" onclick="visitorNonPurch(\'${sref}\')">')
    .replace('data-action="show-new-sale"', 'data-action="showsalemodal"').replace('onclick="showNewSale()"', 'onclick="showSaleModal(null)"')
    .replace('data-saagar-args="${SaagarRenderedComponents.encodeArgs([idx])}" data-action-key="definition-01" class="btn btn-green" style="flex:1">', 'data-action-key="definition-01" class="btn btn-green" style="flex:1" onclick="saveSale(${idx})">')
    .replace('onclick="showNewNonPurchase()"', 'onclick="showNPModal(null)"')
    .replace('data-saagar-args="${SaagarRenderedComponents.encodeArgs([idx])}" data-action-key="definition-01" class="btn btn-primary" style="flex:1">', 'data-action-key="definition-01" class="btn btn-primary" style="flex:1" onclick="saveNP(${idx})">')
    .replace('data-saagar-args="${SaagarRenderedComponents.encodeArgs([m.id,-1])}" data-action-key="definition-01" class="mkt-btn">', 'data-action-key="definition-01" class="mkt-btn" onclick="adjMkt(\'${m.id}\',-1)">')
    .replace('data-saagar-args="${SaagarRenderedComponents.encodeArgs([m.id,1])}" data-action-key="definition-02" class="mkt-btn">', 'data-action-key="definition-02" class="mkt-btn" onclick="adjMkt(\'${m.id}\',1)">')
    .replace('<div class="saagar-table-region--grid" aria-label="Stock variance comparison">\n      <table class="var-tbl" aria-label="Stock variance">', '<table class="var-tbl">')
    .replace('</table></div>\n      <div style="margin-top:12px', '</table>\n      <div style="margin-top:12px')
    .replace('data-action="submit-audit-reject"', 'data-action="submitaudit"').replace('onclick="rejectDsrAudit()"', 'onclick="submitAudit(false)"')
    .replace('data-action="submit-audit-approve"', 'data-action="submitaudit"').replace('onclick="approveDsrAudit()"', 'onclick="submitAudit(true)"')
    .replace('data-action="export-today-csv"', 'data-action="exportallcsv"').replace('onclick="exportTodayCSV()"', 'onclick="exportAllCSV(\'today\')"')
    .replace('         </table></div>`}', '         </table>`}')
    .replace(/\nfunction showNewSale\(\)[\s\S]*?SaagarRenderedComponents\.connect\(document\.body, DsrRenderedPolicy, DsrDelegatedHandlers\);\n\}\n/, '\n')
    .replace('@media print { .dsr-acc-panel[hidden] { display: block !important; } }\n</style>', '@media print { .dsr-acc-panel[hidden] { display: block !important; } }\n\n</style>')
    .replace('\n</style>\n<style id="st-v5-hide-css">', '\n\n</style>\n<style id="st-v5-hide-css">')
    .replace('\n\n\n/* ── INIT ── */', '\n\n/* ── INIT ── */');
}

function restoreQms(source, css) {
  const legacyCss = css
    .replace(/\n\/\* Phase 6G: API-23-safe[\s\S]*?(?=\*,\*::before)/, '')
    .replace(/\n\/\* QMS legacy responsive rules[^\n]+\n/, '\n');
  const marker = '/* ===== Mobile responsive layer (<=640px). Desktop bytes above are untouched. ===== */';
  const split = legacyCss.indexOf(marker);
  const first = legacyCss.slice(0, split).replace(/\n+$/, '');
  const second = legacyCss.slice(split);
  return stripPolicy(source, 'qms')
    .replace('<script src="qms-view.js"></script>\n', '')
    .replace('<link id="phase6g-qms-ui-css" rel="stylesheet" href="qms-ui.css">', `<style>\n\n${first}\n</style>\n<style id="qms-mobile-style">\n${second.replace(/\n+$/, '')}\n</style>`)
    .replace('<body data-saagar-ui data-saagar-width="auto" data-saagar-width-resolved="mobile">\n<script id="phase6g-qms-ui-boot">SaagarUiFoundation.configure(document.body,{mode:\'auto\'});</script>', '<body>')
    .replace('function qmsRenderPresentation(){return SaagarQmsView.decorate(document.body)}\n', '')
    .replace(';applyReadOnly();qmsRenderPresentation()}', ';applyReadOnly()}')
    .replace('</style>\n\n<style id="st-v5-hide-css">', '</style>\n<style id="st-v5-hide-css">');
}

export function restorePrePhase6gFamilyBSource(moduleId, html, css = '', validateHash = true) {
  const restore = { expense: restoreExpense, leave: restoreLeave, cro_audit: restoreCroAudit, tax: restoreTax, dsr: restoreDsr, qms: restoreQms }[moduleId];
  if (!restore) throw new Error(`Unsupported Phase 6G Family-B module: ${moduleId}`);
  const common = '<link rel="stylesheet" href="../../shared/module-mobile-common.css">\n';
  const legacy = '<link id="st-v5-mobile-css" rel="stylesheet" href="../../shared/module-mobile-legacy.css">';
  const restored = restore(html.includes(legacy) ? html : html.replace(common, common + legacy), css);
  if (validateHash && sha256(restored) !== PRE_PHASE6G_FAMILY_B_SHA256[moduleId]) {
    throw new Error(`${moduleId} did not reconstruct to its pre-Phase6G authority (${Buffer.byteLength(restored)} bytes, ${sha256(restored)})`);
  }
  return restored;
}
