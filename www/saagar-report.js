/* ═══════════════════════════════════════════════════════════════════════════
   SAAGAR REPORT ENGINE — enterprise-grade, data-driven A4 PDF reports (offline)
   ───────────────────────────────────────────────────────────────────────────
   Pipeline:  module data (existing shell aggregators) → typed content blocks →
   renderDoc(): NATIVE VECTOR text via jsPDF + jspdf-autotable (tables) and raw
   jsPDF primitives (header/KPI/banner/kv/sign) → Blob → OS share sheet. Text is
   selectable, crisp, tiny, paginates at row boundaries (never mid-row), and the
   rupee symbol renders via an embedded DM Sans subset. No screenshots, no chrome.
   Libs (all local/offline): jspdf.umd.min.js + jspdf.plugin.autotable.min.js +
   fonts/DMSans-normal.js + fonts/DMSans-bold.js.
   ═══════════════════════════════════════════════════════════════════════════ */
(function () {
  'use strict';

  /* ---------- formatters / helpers ---------- */
  function inr(n) { return '₹' + Math.round(Number(n) || 0).toLocaleString('en-IN'); }
  function num(n) { return Math.round(Number(n) || 0).toLocaleString('en-IN'); }
  function pct(n) { return (Math.round(Number(n) || 0)) + '%'; }
  function esc(s) { return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }
  function dash(v) { return (v == null || v === '' || (typeof v === 'number' && isNaN(v))) ? '—' : v; }
  function sleep(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }
  function G(name, fb) { try { return (typeof window[name] === 'function') ? window[name] : fb; } catch (e) { return fb; } }
  function todayIsoSafe() { try { return todayIso(); } catch (e) { return new Date().toISOString().slice(0, 10); } }
  function curDate() { try { if (typeof repDate !== 'undefined' && repDate) return repDate; } catch (e) {} return todayIsoSafe(); }
  function curMonth() { try { if (typeof repMonth !== 'undefined' && repMonth) return repMonth; } catch (e) {} return todayIsoSafe().slice(0, 7); }
  function ownerNm() { try { return ownerName() || ''; } catch (e) { return ''; } }
  function J(k, fb) { try { var v = localStorage.getItem(k); return v ? JSON.parse(v) : fb; } catch (e) { return fb; } }
  function longDate(d) { try { return new Date((d || todayIsoSafe()) + 'T00:00:00').toLocaleDateString('en-IN', { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' }); } catch (e) { return d || ''; } }
  function monthLong(m) { try { return new Date(m + '-01T00:00:00').toLocaleDateString('en-IN', { month: 'long', year: 'numeric' }); } catch (e) { return m || ''; } }
  function stamp() { try { return new Date().toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }); } catch (e) { return ''; } }

  /* Legacy design-system CSS() + HTML-string primitives (lhead/foot/kpiRow/attn/card/kvList/dataTable)
     removed in R6.7 — every report now renders as native vector via renderDoc() + its jsPDF primitives. */

  /* ---------- shared data helpers ---------- */
  var DEN = [2000, 500, 200, 100, 50, 20, 10, 5, 2, 1];
  function denoTotal(map) { var t = 0; if (map) DEN.forEach(function (d) { t += d * (Number(map[d]) || 0); }); return t; }
  function cashDetail(date) {
    var st = J('tanishq_statements', {}) || {}; var rec = st[date] || null;
    var rows = J('gm_expenses', []); var cashIn = 0, cashOut = 0, receipts = [], payments = [];
    (Array.isArray(rows) ? rows : []).forEach(function (e) {
      if (!e || e.void) return;
      if (String(e.date || e.entryDate || e.expenseDate || '').slice(0, 10) !== date) return;
      if (!/cash/i.test(e.mode || e.paymentMode || '')) return;
      var amt = Number(e.amount || e.total || 0) || 0;
      if (String(e.type || 'expense').toLowerCase() === 'income') { cashIn += amt; receipts.push({ cat: e.category || 'Income', desc: e.description || e.desc || '', who: e.vendor || e.source || '', amount: amt }); }
      else { cashOut += amt; payments.push({ cat: e.category || 'Expense', desc: e.description || e.desc || '', who: e.vendor || (e.source === 'petty' ? 'Petty cash' : '') || '', amount: amt }); }
    });
    var opening = rec ? (Number(rec.openingBalance) || 0) : 0;
    var counted = rec ? denoTotal(rec.physDeno) : 0;
    var bankDep = rec ? denoTotal(rec.bankDeno) : 0;
    var expected = opening + cashIn - cashOut;
    return { rec: rec, opening: opening, cashIn: cashIn, cashOut: cashOut, receipts: receipts, payments: payments, counted: counted, bankDep: bankDep, expected: expected, variance: counted - expected, physDeno: (rec && rec.physDeno) || {}, bankDeno: (rec && rec.bankDeno) || {}, balanced: Math.abs(counted - expected) < 1, closed: !!(rec && rec.closed), approved: !!(rec && rec.approved), filledBy: (rec && rec.filledBy) || '', approvedBy: (rec && rec.approvedBy) || '', mismatchReason: (rec && rec.mismatchReason) || '' };
  }
  function storeList() {
    try { var s = window.SaagarMasters && SaagarMasters.stores(); if (s && s.length) return s.map(function (x) { return { code: x.code, key: x.key, name: x.name }; }); } catch (e) {}
    return [{ code: 'WLMHW', key: 'titanworld', name: 'Titan World' }, { code: 'HEMW', key: 'helios', name: 'Helios' }];
  }
  function stockDetailStore(s, date) {
    var blob = J('saagar_stock_' + s.key + '_' + date, null) || J('saagar_stock_' + s.code + '_' + date, null);
    if (!blob || typeof blob !== 'object') return { store: s.name, posted: false };
    var opening = blob.opening || {}, mov = blob.movements || {}, closing = blob.closing || {};
    var brands = Object.keys(opening); if (!brands.length) brands = Object.keys(mov);
    var rows = [], T = { sysOpen: 0, inward: 0, grn: 0, outward: 0, sales: 0, theft: 0, closingSys: 0, closingPhys: 0 };
    function phys(o) { if (!o) return null; var v = ['display', 'storage', 'defective', 'yLoc'].reduce(function (a, f) { var n = Number(o[f]); return a + (isNaN(n) ? 0 : n); }, 0); var any = ['display', 'storage', 'defective', 'yLoc'].some(function (f) { return o[f] != null && o[f] !== ''; }); return any ? v : null; }
    brands.forEach(function (b) {
      var op = opening[b] || {}, m = mov[b] || {}, cl = closing[b] || {};
      var sysOpen = Number(op.systemStock) || 0, inward = Number(m.inward) || 0, grn = Number(m.grn) || 0, outward = Number(m.outward) || 0, sales = Number(m.sales) || 0, theft = Number(m.theft) || 0;
      var closingSys = sysOpen + inward + grn - outward - sales - theft;
      var cp = phys(cl);
      var variance = (cp == null) ? null : cp - closingSys;
      var status = (cp == null) ? 'Pending' : (variance === 0 ? 'OK' : 'Mismatch');
      rows.push({ brand: b, sysOpen: sysOpen, inward: inward, grn: grn, outward: outward, sales: sales, theft: theft, closingSys: closingSys, closingPhys: cp, variance: variance, status: status, __flag: (theft > 0 || (variance != null && variance !== 0)) });
      T.sysOpen += sysOpen; T.inward += inward; T.grn += grn; T.outward += outward; T.sales += sales; T.theft += theft; T.closingSys += closingSys; T.closingPhys += (cp || 0);
    });
    return { store: s.name, code: s.code, posted: true, rows: rows, totals: T, openingLocked: !!blob.openingLocked, movementsSubmitted: !!blob.movementsSubmitted, closingLocked: !!blob.closingLocked };
  }
  function expenseMonth(month) {
    var rows = J('gm_expenses', []); var inc = {}, exp = {}, incTot = 0, expTot = 0, firms = {};
    (Array.isArray(rows) ? rows : []).forEach(function (e) {
      if (!e || e.void) return; if (String(e.date || e.entryDate || e.expenseDate || '').slice(0, 7) !== month) return;
      var amt = Number(e.amount || e.total || 0) || 0, cat = e.category || 'Other';
      if (String(e.type || 'expense').toLowerCase() === 'income') { inc[cat] = (inc[cat] || 0) + amt; incTot += amt; }
      else { exp[cat] = (exp[cat] || 0) + amt; expTot += amt; var fk = e.firm || 'Unassigned'; if (!firms[fk]) firms[fk] = { firm: fk, amount: 0, count: 0 }; firms[fk].amount += amt; firms[fk].count++; }
    });
    return { inc: inc, exp: exp, incTot: incTot, expTot: expTot, net: incTot - expTot, firms: Object.keys(firms).map(function (k) { return firms[k]; }).sort(function (a, b) { return b.amount - a.amount; }) };
  }
  function monthQms(month) {
    var parts = String(month).split('-'), y = +parts[0], mo = +parts[1]; if (!y || !mo) return { sales: 0, purchases: 0, walkins: 0, conversion: 0 };
    var dim = new Date(y, mo, 0).getDate(), s = 0, p = 0, w = 0, fn = G('computeQmsDay', null);
    if (fn) for (var dd = 1; dd <= dim; dd++) { var q = fn(month + '-' + String(dd).padStart(2, '0')); s += q.sales || 0; p += q.purchases || 0; w += q.total || 0; }
    return { sales: s, purchases: p, walkins: w, conversion: w ? Math.round(p / w * 100) : 0 };
  }
  /* ---------- P1-47 week helpers (LOCAL date math — NEVER toISOString; IST +5:30 shifts the window a day) ---------- */
  function isoOf(dt) { return dt.getFullYear() + '-' + String(dt.getMonth() + 1).padStart(2, '0') + '-' + String(dt.getDate()).padStart(2, '0'); }
  function isoWeek(anchorIso) {
    var p = String(anchorIso || todayIsoSafe()).slice(0, 10).split('-');
    var base = new Date(+p[0], (+p[1] || 1) - 1, +p[2] || 1);
    var dow = (base.getDay() + 6) % 7;                              // Mon=0 .. Sun=6
    var mon = new Date(base.getFullYear(), base.getMonth(), base.getDate() - dow);
    var days = [];
    for (var i = 0; i < 7; i++) days.push(isoOf(new Date(mon.getFullYear(), mon.getMonth(), mon.getDate() + i)));
    return { start: days[0], end: days[6], days: days };
  }
  function weekAgg(days) {
    var fn = G('computeQmsDay', null);
    var acc = { walkins: 0, purchases: 0, sales: 0, nonPurchase: 0, conversion: 0, byCro: {}, perDay: [] };
    (Array.isArray(days) ? days : []).forEach(function (dy) {
      var q = fn ? (fn(dy) || {}) : {};
      var w = Number(q.total) || 0, pu = Number(q.purchases) || 0, sa = Number(q.sales) || 0, np = Number(q.nonPurchase) || 0;
      acc.walkins += w; acc.purchases += pu; acc.sales += sa; acc.nonPurchase += np;
      (Array.isArray(q.byCro) ? q.byCro : []).forEach(function (c) {
        var k = c.cro || 'Unassigned';
        if (!acc.byCro[k]) acc.byCro[k] = { cro: k, walkins: 0, purchases: 0, sales: 0 };
        acc.byCro[k].walkins += Number(c.walkins) || 0; acc.byCro[k].purchases += Number(c.purchases) || 0; acc.byCro[k].sales += Number(c.sales) || 0;
      });
      acc.perDay.push({ date: dy, walkins: w, purchases: pu, sales: sa, conv: w ? Math.round(pu / w * 100) : 0 });
    });
    acc.conversion = acc.walkins ? Math.round(acc.purchases / acc.walkins * 100) : 0;   // recomputed on totals, NOT averaged
    return acc;
  }
  function deltaOf(cur, prev) { return { abs: cur - prev, pct: prev ? Math.round((cur - prev) / prev * 100) : null, up: (cur - prev) >= 0 }; }
  function weekLabel(startIso, endIso) {
    try {
      var s = new Date(startIso + 'T00:00:00'), e = new Date(endIso + 'T00:00:00');
      return s.toLocaleDateString('en-IN', { day: '2-digit', month: 'short' }) + ' – ' + e.toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
    } catch (err) { return (startIso || '') + ' – ' + (endIso || ''); }
  }

  /* ---------- per-report builders → { orientation, pages:[innerHtml,...] } ---------- */
  var BUILDERS = {

    /* ===== ⭐ OWNER DAILY BRIEF (portrait, 1 page) ===== */
    ownerBrief: function (o) {
      var date = o.date || curDate();
      var b = G('buildTodayBrief', function () { return {}; })(date);
      var qms = G('computeQmsDay', function () { return { byCro: [] }; })(date);
      var dsr = G('computeDsrDay', function () { return {}; })(date);
      var cash = cashDetail(date);
      var aging = G('computeServiceAging', function () { return {}; })();
      var tax = G('computeTaxStatus', function () { return { upcoming: [] }; })();
      var leave = G('computeLeaveDay', function () { return { items: [] }; })(date);
      var stores = storeList().map(function (s) { return stockDetailStore(s, date); });

      var flags = [];
      if (cash.rec && !cash.balanced) flags.push('Cash ' + (cash.variance < 0 ? 'short' : 'over') + ' ' + inr(Math.abs(cash.variance)) + ' (counted ' + inr(cash.counted) + ' vs expected ' + inr(cash.expected) + ')');
      if (cash.rec && !cash.closed) flags.push('Cash sheet not closed for ' + longDate(date).split(',')[0]);
      stores.forEach(function (st) {
        if (!st.posted) { flags.push(st.store + ' stock not posted today'); return; }
        var theft = st.totals.theft, varc = st.rows.filter(function (r) { return r.variance != null && r.variance !== 0; }).length;
        if (theft > 0) flags.push(st.store + ' theft — ' + theft + ' unit(s) flagged');
        else if (varc > 0) flags.push(st.store + ' stock variance on ' + varc + ' brand(s)');
      });
      if ((b.qmsSales || 0) === 0 && (b.qmsWalkins || 0) > 0) flags.push('Zero sales despite ' + b.qmsWalkins + ' walk-ins');
      if ((b.qmsWalkins || 0) >= 10 && (b.qmsConversion || 0) < 20) flags.push('Low conversion ' + (b.qmsConversion || 0) + '% on ' + b.qmsWalkins + ' walk-ins');
      if ((aging.b16 || 0) > 0) flags.push('Service backlog — ' + aging.b16 + ' case(s) aged 16+ days');
      if ((b.taxOverdue || 0) > 0) flags.push(b.taxOverdue + ' overdue tax item(s)' + (tax.upcoming[0] && tax.upcoming[0].status === 'Overdue' ? ' — ' + tax.upcoming[0].item : ''));
      if ((b.groomingTodayChecks || 0) > 0 && (b.groomingTodayAvg || 0) < 60) flags.push('Grooming low — store avg ' + b.groomingTodayAvg + '%');
      if (b.backupDays != null && b.backupDays > 7) flags.push('Backup overdue — ' + b.backupDays + ' days');

      var blocks = [{ t: 'header', title: 'DAILY OWNER BRIEF', sub: 'Titan World + Helios · Latur', period: longDate(date) }];
      blocks.push({ t: 'attn', flags: flags });
      blocks.push({ t: 'kpi', cols: 5, items: [
        { label: 'Net Sales', value: inr(b.qmsSales), sub: (dsr.salesCnt ? num(dsr.salesCnt) + ' bills (DSR)' : ''), hero: true },
        { label: 'Conversion', value: pct(b.qmsConversion) },
        { label: 'Walk-ins → Buy', value: num(b.qmsWalkins) + ' → ' + num(b.qmsPurchases), sub: ((b.qmsWalkins || 0) - (b.qmsPurchases || 0)) + ' not converted' },
        { label: 'Cash', value: cash.rec ? inr(cash.counted) : '—', sub: cash.rec ? (cash.balanced ? 'Reconciled' : 'Mismatch') : 'Not counted', subClass: cash.rec ? (cash.balanced ? 'up' : 'down') : 'neu' },
        { label: 'Open Service', value: num(b.serviceOpen), sub: (aging.b16 || 0) + ' aged 16d+', subClass: (aging.b16 || 0) > 0 ? 'down' : 'neu' }
      ] });
      blocks.push({ t: 'section', title: 'Sales & Conversion — by CRO' });
      var croRows = (qms.byCro || []).slice(0, 8);
      if (croRows.length) {
        blocks.push({ t: 'table',
          head: [['CRO', 'Walk-ins', 'Purchases', 'Conv %', 'Sales ₹']],
          body: croRows.map(function (c) { return [c.cro, num(c.walkins), num(c.purchases), (c.walkins ? Math.round(c.purchases / c.walkins * 100) + '%' : '—'), inr(c.sales)]; }),
          money: [4],
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 80, halign: 'right' }, 2: { cellWidth: 80, halign: 'right' }, 3: { cellWidth: 70, halign: 'right' }, 4: { cellWidth: 110, halign: 'right' } },
          foot: [['Total', num(qms.total), num(qms.purchases), pct(qms.conversion), inr(qms.sales)]] });
      } else { blocks.push({ t: 'empty', text: 'No walk-ins recorded today.' }); }
      blocks.push({ t: 'section', title: 'Cash Position', tag: cash.rec ? (cash.balanced ? { cls: 'ok', txt: 'Reconciled' } : { cls: 'bad', txt: 'Mismatch' }) : { cls: 'warn', txt: 'Pending' } });
      if (cash.rec) {
        blocks.push({ t: 'kv', cols: 2, pairs: [
          ['Opening cash', inr(cash.opening)], ['+ Cash receipts', inr(cash.cashIn)],
          ['− Cash payments', inr(cash.cashOut)], ['Expected closing', inr(cash.expected), 'big'],
          ['Counted (physical)', inr(cash.counted)], ['Variance', cash.balanced ? '₹0 · Balanced' : inr(cash.variance)]
        ] });
      } else { blocks.push({ t: 'empty', text: 'Cash sheet not filled today.' }); }
      var stockPairs = [];
      stores.forEach(function (st) {
        if (!st.posted) { stockPairs.push([st.store, 'Not posted']); return; }
        var theft = st.totals.theft, varc = st.rows.filter(function (r) { return r.variance != null && r.variance !== 0; }).length;
        stockPairs.push([st.store, theft > 0 ? (theft + ' units theft') : varc > 0 ? (varc + ' brand variance') : 'Posted · no variance']);
      });
      stockPairs.push(['Sales units (day)', num(stores.reduce(function (a, s) { return a + (s.posted ? s.totals.sales : 0); }, 0))]);
      var stockAlerts = stores.reduce(function (a, s) { return a + (s.posted ? (s.totals.theft > 0 ? 1 : 0) : 1); }, 0);
      blocks.push({ t: 'section', title: 'Stock — Both Stores', tag: stockAlerts ? { cls: 'bad', txt: stockAlerts + ' alert' } : { cls: 'ok', txt: 'Clean' } });
      blocks.push({ t: 'kv', cols: 2, pairs: stockPairs });
      var leaveStr = leave.items.length ? leave.items.map(function (l) { return l.name + ' (' + l.type + ')'; }).join(', ') : 'None';
      blocks.push({ t: 'section', title: 'Staff & Grooming', tag: (b.groomingTodayAvg || 100) < 80 ? { cls: 'warn', txt: (b.groomingTodayAvg || 0) + '% avg' } : { cls: 'ok', txt: (b.groomingTodayAvg || 0) + '% avg' } });
      blocks.push({ t: 'kv', cols: 2, pairs: [
        ['Present today', num(dsr.present)], ['On leave', leaveStr],
        ['Grooming average', (b.groomingTodayChecks ? b.groomingTodayAvg + '% · ' + b.groomingTodayPass + '/' + b.groomingTodayChecks + ' pass' : '—')],
        ['Expenses today', inr(b.expenseTodayAmount) + ' · ' + num(b.expenseToday) + ' entries']
      ] });
      var nextTax = (tax.upcoming || [])[0];
      blocks.push({ t: 'section', title: 'Compliance & Service', tag: ((b.taxOverdue || 0) > 0 || (aging.b16 || 0) > 0) ? { cls: 'bad', txt: 'Action' } : { cls: 'ok', txt: 'Clear' } });
      blocks.push({ t: 'kv', cols: 2, pairs: [
        ['Tax overdue', (b.taxOverdue || 0) > 0 ? (b.taxOverdue + (nextTax && nextTax.status === 'Overdue' ? ' · ' + nextTax.item : '')) : '0'],
        ['Due this week', num(b.taxDueWeek)], ['Open service cases', num(b.serviceOpen)],
        ['Aged 16+ days', String(aging.b16 || 0)], ['Last data backup', (b.backupDays == null ? '—' : b.backupDays === 0 ? 'Today' : b.backupDays + ' days ago')]
      ] });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== DAILY CASH STATEMENT (portrait, 1 page) ===== */
    cashStatement: function (o) {
      var date = o.date || curDate();
      var c = cashDetail(date);
      var hdr = { t: 'header', title: 'DAILY CASH STATEMENT', sub: 'Saagar Traders · Latur', period: longDate(date) };
      if (!c.rec) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'Cash statement not filled for this date. Opening carry-forward: ' + inr(c.opening) }] };
      var statusTxt = c.closed ? (c.approved ? 'Locked & approved' : 'Closed · awaiting approval') : 'Open';
      var blocks = [{ t: 'header', title: 'DAILY CASH STATEMENT', sub: 'Saagar Traders · Latur', period: longDate(date), chip: statusTxt, chipKind: (c.balanced && c.closed) ? 'ok' : 'draft' }];
      blocks.push({ t: 'kpi', cols: 5, items: [
        { label: 'Opening', value: inr(c.opening) },
        { label: 'Cash Receipts', value: inr(c.cashIn) },
        { label: 'Cash Payments', value: inr(c.cashOut) },
        { label: 'Expected Closing', value: inr(c.expected), hero: true },
        { label: 'Counted', value: inr(c.counted), sub: c.balanced ? 'Balanced' : inr(c.variance), subClass: c.balanced ? 'up' : 'down' }
      ] });
      var denoRows = DEN.filter(function (d) { return (Number(c.physDeno[d]) || 0) || (Number(c.bankDeno[d]) || 0); });
      blocks.push({ t: 'section', title: 'Denomination Count' });
      if (denoRows.length) {
        blocks.push({ t: 'table',
          head: [['Denom', 'Phys count', 'Phys value', 'Bank count', 'Bank value']],
          body: denoRows.map(function (d) { var pc = Number(c.physDeno[d]) || 0, bc = Number(c.bankDeno[d]) || 0; return [inr(d), num(pc), inr(d * pc), num(bc), inr(d * bc)]; }),
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 92, halign: 'right' }, 2: { cellWidth: 112, halign: 'right' }, 3: { cellWidth: 92, halign: 'right' }, 4: { cellWidth: 112, halign: 'right' } },
          foot: [['Total', '', inr(c.counted), '', inr(c.bankDep)]] });
      } else { blocks.push({ t: 'empty', text: 'No denominations counted.' }); }
      function ledger(title, list, tag) {
        blocks.push({ t: 'section', title: title, tag: tag });
        if (!list.length) { blocks.push({ t: 'empty', text: 'None today.' }); return; }
        blocks.push({ t: 'table',
          head: [['Category', 'Description', 'Vendor / Source', 'Amount ₹']],
          body: list.map(function (x) { return [trunc(x.cat, 22), trunc(x.desc || '—', 40), trunc(x.who || '—', 24), inr(x.amount)]; }),
          money: [3],
          colStyles: { 0: { cellWidth: 110 }, 1: { cellWidth: 'auto' }, 2: { cellWidth: 130 }, 3: { cellWidth: 98, halign: 'right' } },
          foot: [['Total', '', '', inr(list.reduce(function (a, x) { return a + x.amount; }, 0))]] });
      }
      ledger('Cash Receipts', c.receipts, { cls: 'ok', txt: inr(c.cashIn) });
      ledger('Cash Payments', c.payments, { cls: 'warn', txt: inr(c.cashOut) });
      blocks.push({ t: 'section', title: 'Reconciliation', tag: c.balanced ? { cls: 'ok', txt: 'Balanced' } : { cls: 'bad', txt: 'Mismatch' } });
      blocks.push({ t: 'kv', cols: 2, pairs: [
        ['Expected closing cash', inr(c.expected), 'big'], ['Counted physical cash', inr(c.counted)],
        ['Variance', c.balanced ? '₹0 · Balanced' : inr(c.variance)], ['Deposited to bank', inr(c.bankDep)],
        ['Retained in drawer', inr(c.counted - c.bankDep)], ['Filled by', c.filledBy || '—'],
        ['Approved by', c.approvedBy || (c.closed ? 'Pending' : '—')]
      ] });
      if (!c.balanced && c.mismatchReason) blocks.push({ t: 'note', text: 'Mismatch reason: ' + c.mismatchReason, color: PAL.RED });
      blocks.push({ t: 'note', text: 'Card / UPI / Bank entries appear in the P&L, not in cash reconciliation.' });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== DAILY STOCK CLOSING REGISTER (landscape, 1 page per store) ===== */
    stockRegister: function (o) {
      var date = o.date || curDate();
      var stores = storeList().map(function (s) { return stockDetailStore(s, date); });
      var blocks = [{ t: 'header', title: 'STOCK CLOSING REGISTER', sub: 'Daily inventory integrity — system vs physical', period: longDate(date) }];
      stores.forEach(function (st, i) {
        if (i > 0) blocks.push({ t: 'spacer', h: 6 });
        blocks.push({ t: 'section', title: st.store + (st.code ? ' (' + st.code + ')' : ''), tag: st.posted ? (st.closingLocked ? { cls: 'ok', txt: 'Closing locked' } : { cls: 'warn', txt: 'Open' }) : { cls: 'bad', txt: 'Not posted' } });
        if (!st.posted) { blocks.push({ t: 'empty', text: st.store + ' stock not posted for this date.' }); return; }
        var T = st.totals, varBrands = st.rows.filter(function (r) { return r.variance != null && r.variance !== 0; }).length;
        blocks.push({ t: 'kpi', cols: 5, items: [
          { label: 'Brands', value: num(st.rows.length) },
          { label: 'Opening (sys)', value: num(T.sysOpen) },
          { label: 'Sales units', value: num(T.sales) },
          { label: 'Theft units', value: num(T.theft), sub: T.theft > 0 ? 'flagged' : 'clean', subClass: T.theft > 0 ? 'down' : 'up', hero: true },
          { label: 'Variance brands', value: num(varBrands), sub: varBrands ? 'check' : 'none', subClass: varBrands ? 'down' : 'up' }
        ] });
        blocks.push({ t: 'table',
          head: [['Brand', 'Open(sys)', 'Inward', 'GRN', 'Outward', 'Sales', 'Theft', 'Close-Sys', 'Close-Phys', 'Variance', 'Status']],
          body: st.rows.map(function (r) { return [trunc(r.brand, 22), num(r.sysOpen), num(r.inward), num(r.grn), num(r.outward), num(r.sales), r.theft > 0 ? num(r.theft) : '—', num(r.closingSys), r.closingPhys == null ? '—' : num(r.closingPhys), (r.variance == null || r.variance === 0) ? '—' : (r.variance > 0 ? '+' : '') + r.variance, r.status]; }),
          flagRows: st.rows.map(function (r) { return r.theft > 0 || (r.variance != null && r.variance !== 0); }),
          pills: [10],
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 70, halign: 'right' }, 2: { cellWidth: 58, halign: 'right' }, 3: { cellWidth: 50, halign: 'right' }, 4: { cellWidth: 62, halign: 'right' }, 5: { cellWidth: 54, halign: 'right' }, 6: { cellWidth: 52, halign: 'right' }, 7: { cellWidth: 70, halign: 'right' }, 8: { cellWidth: 76, halign: 'right' }, 9: { cellWidth: 64, halign: 'right' }, 10: { cellWidth: 78, halign: 'center' } },
          foot: [['TOTAL (' + st.rows.length + ')', num(T.sysOpen), num(T.inward), num(T.grn), num(T.outward), num(T.sales), num(T.theft), num(T.closingSys), num(T.closingPhys), '', '']] });
      });
      return { orientation: 'landscape', blocks: blocks };
    },

    /* ===== DSR — DAILY SALES REGISTER (portrait) ===== */
    dsrRegister: function (o) {
      var date = o.date || curDate();
      var d = G('computeDsrDay', function () { return { staff: [], present: 0, salesAmt: 0, salesCnt: 0 }; })(date);
      var recs = [], pre = 'saagar_dsr_' + date + '_';
      for (var i = 0; i < localStorage.length; i++) { var k = localStorage.key(i); if (k && k.indexOf(pre) === 0) { var r = J(k, {}) || {}; r.__name = r.staffName || k.slice(pre.length).replace(/_/g, ' '); recs.push(r); } }
      var hdr = { t: 'header', title: 'DAILY SALES REGISTER', sub: 'Staff sales & activity · Latur', period: longDate(date) };
      if (!d.present) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'No sales register submitted for this date.' }] };
      var avg = d.salesCnt ? Math.round(d.salesAmt / d.salesCnt) : 0;
      var blocks = [hdr, { t: 'kpi', cols: 5, items: [
        { label: 'Net Sales', value: inr(d.salesAmt), hero: true },
        { label: 'Bills', value: num(d.salesCnt) },
        { label: 'Staff Present', value: num(d.present) },
        { label: 'Top Performer', value: (d.staff[0] && d.staff[0].name) || '—', sub: d.staff[0] ? inr(d.staff[0].salesAmt) : '' },
        { label: 'Avg / Bill', value: inr(avg) }
      ] }];
      blocks.push({ t: 'section', title: 'Staff Sales Summary' });
      blocks.push({ t: 'table',
        head: [['Staff', 'Role', 'Bills', 'Sales ₹', 'Submitted']],
        body: d.staff.map(function (s) { var r = recs.filter(function (x) { return x.__name === s.name; })[0] || {}; return [s.name, s.role || '—', num(s.salesCnt), inr(s.salesAmt), r.submitted ? ('Yes ' + (r.submitTime || '')) : '—']; }),
        money: [3],
        colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 84 }, 2: { cellWidth: 48, halign: 'right' }, 3: { cellWidth: 90, halign: 'right' }, 4: { cellWidth: 96, halign: 'center' } },
        foot: [['Total', '', num(d.salesCnt), inr(d.salesAmt), '']] });
      var bills = []; recs.forEach(function (r) { (Array.isArray(r.sales) ? r.sales : []).forEach(function (s) { var a = Number(s.amount) || 0; if (a > 0) bills.push({ billNo: s.billNo || '—', staff: r.__name, product: s.product || '—', customer: s.customer || '—', source: (/qms/i.test(s.source || '') ? 'QMS' : 'Manual'), amount: a }); }); });
      var billSum = bills.reduce(function (a, b) { return a + b.amount; }, 0);
      blocks.push({ t: 'section', title: 'Bill-Level Detail', tag: { cls: 'ok', txt: num(bills.length) + ' bills' } });
      if (bills.length) {
        blocks.push({ t: 'table',
          head: [['Bill No', 'Staff', 'Product', 'Customer', 'Source', 'Amount ₹']],
          body: bills.map(function (b) { return [b.billNo, trunc(b.staff, 18), trunc(b.product, 26), trunc(b.customer, 22), b.source, inr(b.amount)]; }),
          raw: bills.map(function (b) { return [0, 0, 0, 0, 0, b.amount]; }),
          money: [5],
          colStyles: { 0: { cellWidth: 56 }, 1: { cellWidth: 'auto' }, 2: { cellWidth: 'auto' }, 3: { cellWidth: 86 }, 4: { cellWidth: 50, halign: 'center' }, 5: { cellWidth: 80, halign: 'right' } },
          foot: [['Total', '', '', '', '', inr(billSum)]] });
      } else { blocks.push({ t: 'empty', text: 'No bills recorded.' }); }
      var nps = []; recs.forEach(function (r) { (Array.isArray(r.nonpurch) ? r.nonpurch : []).forEach(function (n) { nps.push({ customer: n.customer || n.name || '—', mobile: n.mobile || '—', reason: n.reason || '—' }); }); });
      blocks.push({ t: 'section', title: 'Lost Walk-ins (Non-Purchase)', tag: nps.length ? { cls: 'warn', txt: num(nps.length) } : { cls: 'ok', txt: '0' } });
      if (nps.length) { blocks.push({ t: 'table', head: [['Customer', 'Mobile', 'Reason']], body: nps.map(function (n) { return [trunc(n.customer, 30), n.mobile, trunc(n.reason, 44)]; }), colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 96 }, 2: { cellWidth: 'auto' } } }); }
      else { blocks.push({ t: 'empty', text: 'No lost walk-ins logged.' }); }
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== QMS — DAILY QUEUE & CONVERSION (landscape) ===== */
    qmsReport: function (o) {
      var date = o.date || curDate();
      var q = G('computeQmsDay', function () { return { byCro: [], total: 0 }; })(date);
      var st = J('retail_queue_management_v1', {}) || {}; var custs = Array.isArray(st.customers) ? st.customers : [], cros = Array.isArray(st.cros) ? st.cros : [];
      var nameOf = function (id) { var c = cros.filter(function (x) { return x.id === id; })[0]; return c ? c.name : 'Unassigned'; };
      var day = custs.filter(function (c) { return String(c.entryTime || '').slice(0, 10) === date; });
      var hdr = { t: 'header', title: 'QUEUE & CONVERSION REPORT', sub: 'Walk-in funnel · Latur', period: longDate(date) };
      if (!q.total) return { orientation: 'landscape', blocks: [hdr, { t: 'empty', text: 'No queue activity recorded for this date.' }] };
      var top = (q.byCro || []).slice().sort(function (a, b) { return (b.purchases / Math.max(1, b.walkins)) - (a.purchases / Math.max(1, a.walkins)); })[0];
      var blocks = [hdr, { t: 'kpi', cols: 6, items: [
        { label: 'Walk-ins', value: num(q.total) },
        { label: 'Purchases', value: num(q.purchases) },
        { label: 'Conversion', value: pct(q.conversion), hero: true },
        { label: 'Sales ₹', value: inr(q.sales) },
        { label: 'Non-Purchase', value: num(q.nonPurchase) },
        { label: 'Top CRO', value: (top && top.cro) || '—', sub: top ? pct(Math.round(top.purchases / Math.max(1, top.walkins) * 100)) : '' }
      ] }];
      var croFlags = [], croBody = (q.byCro || []).map(function (c, i) { croFlags[i] = (c.walkins >= 3 && c.purchases / c.walkins < 0.2); return [c.cro, num(c.walkins), num(c.purchases), (c.walkins ? Math.round(c.purchases / c.walkins * 100) + '%' : '—'), inr(c.sales)]; });
      blocks.push({ t: 'section', title: 'CRO Performance' });
      blocks.push({ t: 'table',
        head: [['CRO', 'Walk-ins', 'Purchases', 'Conv %', 'Sales ₹']],
        body: croBody, money: [4], flagRows: croFlags,
        colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 96, halign: 'right' }, 2: { cellWidth: 96, halign: 'right' }, 3: { cellWidth: 84, halign: 'right' }, 4: { cellWidth: 130, halign: 'right' } },
        foot: [['Total', num(q.total), num(q.purchases), pct(q.conversion), inr(q.sales)]] });
      var lr = {}; day.filter(function (c) { return /non.?purchase/i.test(c.outcome || ''); }).forEach(function (c) { var k = c.lostReason || 'Not specified'; if (!lr[k]) lr[k] = { reason: k, count: 0, val: 0 }; lr[k].count++; lr[k].val += Number(c.lostValue) || 0; });
      var lrRows = Object.keys(lr).map(function (k) { return lr[k]; }).sort(function (a, b) { return b.count - a.count; });
      blocks.push({ t: 'section', title: 'Lost Sales — by reason', tag: lrRows.length ? { cls: 'warn', txt: num(q.nonPurchase) } : { cls: 'ok', txt: '0' } });
      if (lrRows.length) { blocks.push({ t: 'table', head: [['Reason', 'Count', 'Est. Lost ₹']], body: lrRows.map(function (x) { return [trunc(x.reason, 48), num(x.count), inr(x.val)]; }), money: [2], colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 90, halign: 'right' }, 2: { cellWidth: 150, halign: 'right' } } }); }
      else { blocks.push({ t: 'empty', text: 'No lost-sale reasons logged.' }); }
      var pb = day.filter(function (c) { return c.outcome === 'Purchase'; }).map(function (c) { return { q: c.queueNo || '—', name: c.name || '—', bill: c.billNo || '—', cat: c.purchaseCategory || '—', pay: c.paymentMode || '—', cro: nameOf(c.assignedCroId), amt: Number(c.purchaseAmount) || 0 }; });
      var pbSum = pb.reduce(function (a, b) { return a + b.amt; }, 0);
      blocks.push({ t: 'section', title: 'Purchase Bills', tag: { cls: 'ok', txt: num(pb.length) } });
      if (pb.length) {
        blocks.push({ t: 'table',
          head: [['Queue', 'Customer', 'Bill', 'Category', 'Pay', 'CRO', 'Amount ₹']],
          body: pb.map(function (c) { return [c.q, trunc(c.name, 22), c.bill, trunc(c.cat, 18), trunc(c.pay, 12), trunc(c.cro, 18), inr(c.amt)]; }),
          raw: pb.map(function (c) { return [0, 0, 0, 0, 0, 0, c.amt]; }),
          money: [6],
          colStyles: { 0: { cellWidth: 50, halign: 'right' }, 1: { cellWidth: 'auto' }, 2: { cellWidth: 72 }, 3: { cellWidth: 104 }, 4: { cellWidth: 72 }, 5: { cellWidth: 112 }, 6: { cellWidth: 98, halign: 'right' } },
          foot: [['Total', '', '', '', '', '', inr(pbSum)]] });
      } else { blocks.push({ t: 'empty', text: 'No purchase bills.' }); }
      return { orientation: 'landscape', blocks: blocks };
    },

    /* ===== CRO — DAILY AUDIT SCORECARD (landscape) ===== */
    croAudit: function (o) {
      var date = o.date || curDate();
      var all = J('cro_audits_v3', []); var list = Array.isArray(all) ? all.filter(function (a) { return String(a.date || '').slice(0, 10) === date; }) : [];
      var hdr0 = { t: 'header', title: 'CRO DAILY AUDIT SCORECARD', sub: 'Saagar Traders · Latur', period: longDate(date) };
      if (!list.length) return { orientation: 'landscape', blocks: [hdr0, { t: 'empty', text: 'No CRO audits recorded for this date.' }] };
      var TASKS = [['t1', 'Open Stk'], ['t2', 'Display'], ['t3', 'Sale Rec'], ['t4', 'Non-Pur'], ['t5', 'NPS'], ['t6', 'Reviews'], ['t7', 'Mktg'], ['t8', 'Close Stk'], ['t9', 'Groom'], ['t10', 'Disc.']];
      function pts(a, t) { return (a.tasks && a.tasks[t] && a.tasks[t].pts != null) ? a.tasks[t].pts : 0; }
      var sm = list[0].sm || '', store = list[0].store || '';
      var avg = Math.round(list.reduce(function (s, a) { return s + (Number(a.total) || 0); }, 0) / list.length);
      var top = list.slice().sort(function (a, b) { return (Number(b.total) || 0) - (Number(a.total) || 0); })[0];
      var needs = list.filter(function (a) { return (Number(a.total) || 0) < 60; }).length;
      var groomAvg = Math.round(list.reduce(function (s, a) { return s + (Number(a.tasks && a.tasks.t9 && a.tasks.t9.groomingPct) || 0); }, 0) / list.length);
      var sorted = list.slice().sort(function (a, b) { return (Number(b.total) || 0) - (Number(a.total) || 0); });
      var blocks = [{ t: 'header', title: 'CRO DAILY AUDIT SCORECARD', sub: (store || 'Saagar Traders') + ' · Auditor ' + (sm || '—'), period: longDate(date) }];
      blocks.push({ t: 'kpi', cols: 5, items: [
        { label: 'CROs Audited', value: num(list.length) },
        { label: 'Avg Score', value: avg + '/100', hero: true },
        { label: 'Top CRO', value: (top && top.cro) || '—', sub: top ? (top.total + '/100') : '', subClass: 'up' },
        { label: 'Needs Attention', value: num(needs), sub: needs ? '< 60' : 'none', subClass: needs ? 'down' : 'up' },
        { label: 'Avg Grooming', value: groomAvg + '%' }
      ] });
      blocks.push({ t: 'section', title: 'Scorecard Summary', tag: needs ? { cls: 'bad', txt: needs + ' below 60' } : { cls: 'ok', txt: 'All passing' } });
      blocks.push({ t: 'table',
        head: [['CRO', 'Score', 'Grade', 'Grooming', 'Weakest Task']],
        body: sorted.map(function (a) { var weak = '—', low = 99; TASKS.forEach(function (t) { var p = pts(a, t[0]); if (p < low) { low = p; weak = t[1]; } }); return [a.cro, (Number(a.total) || 0) + '/100', a.grade || '—', ((a.tasks && a.tasks.t9 && a.tasks.t9.groomingPct) || 0) + '%', weak + ' (' + low + ')']; }),
        flagRows: sorted.map(function (a) { return (Number(a.total) || 0) < 60; }),
        colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 70, halign: 'right' }, 2: { cellWidth: 70 }, 3: { cellWidth: 84, halign: 'right' }, 4: { cellWidth: 170 } } });
      blocks.push({ t: 'section', title: 'Task Breakdown — points / 10' });
      var mhead = ['CRO'].concat(TASKS.map(function (t) { return t[1]; })).concat(['Total']);
      var mcolStyles = { 0: { cellWidth: 86 } }; for (var ci = 1; ci <= 10; ci++) mcolStyles[ci] = { halign: 'right' }; mcolStyles[11] = { cellWidth: 50, halign: 'right' };
      var mtotRow = ['Avg']; TASKS.forEach(function (t) { mtotRow.push(String(Math.round(sorted.reduce(function (s, a) { return s + pts(a, t[0]); }, 0) / sorted.length))); }); mtotRow.push(String(avg));
      blocks.push({ t: 'table',
        head: [mhead],
        body: sorted.map(function (a) { var row = [a.cro]; TASKS.forEach(function (t) { row.push(num(pts(a, t[0]))); }); row.push(num(Number(a.total) || 0)); return row; }),
        flagRows: sorted.map(function (a) { return (Number(a.total) || 0) < 60; }),
        colStyles: mcolStyles,
        foot: [mtotRow] });
      return { orientation: 'landscape', blocks: blocks };
    },

    /* ===== PAYROLL — SALARY REGISTER (landscape) ===== */
    payrollRegister: function (o) {
      o = o || {};
      // Prefer EXACT figures passed live from the payroll module (opts.rows). Fall back to the
      // mirrored localStorage snapshot when called with no data (e.g. the shell Reports hub) —
      // that path is byte-for-byte the original behaviour.
      var period, locked, T, body = [], raw = [], items, fromOpts = Array.isArray(o.rows);
      if (fromOpts) {
        period = o.period || ''; locked = !!o.locked;
        items = o.rows.map(function (r) { return { empId: r.empId || '—', name: r.name || '—', designation: r.designation || '—', gross: Number(r.gross) || 0, ot: Number(r.ot) || 0, pt: Number(r.pt) || 0, pf: Number(r.pf) || 0, esic: Number(r.esic) || 0, advance: Number(r.advance) || 0, net: Number(r.net) || 0 }; });
        var ot0 = o.totals || {};
        T = { gross: Number(ot0.gross) || 0, ot: Number(ot0.ot) || 0, pt: Number(ot0.pt) || 0, pf: Number(ot0.pf) || 0, esic: Number(ot0.esic) || 0, adv: Number(ot0.advance) || 0, net: Number(ot0.net) || 0 };
      } else {
        var p = J('payroll_suite_v1_2026', {}) || {}, meta = p.meta || {}, rows = Array.isArray(p.rows) ? p.rows : [];
        period = (meta.month || '') + ' ' + (meta.year || '');
        var status = (meta.run && meta.run.status) || ((p.runs && Object.keys(p.runs).length) ? 'locked' : 'draft'); locked = /lock/i.test(status);
        T = { gross: 0, ot: 0, pt: 0, pf: 0, esic: 0, adv: 0, net: 0 }; items = [];
        rows.forEach(function (r) {
          var gross = Number(r.grossPayable != null ? r.grossPayable : (r.gross || 0)) || 0, pt = Number(r.pt) || 0, pf = Number(r.pf) || 0, es = Number(r.esic) || 0, adv = Number(r.advance) || 0, net = Number(r.net != null ? r.net : (r.netPay || 0)) || 0;
          var ot = Math.max(0, Math.round(net + pt + pf + es + adv - gross));
          T.gross += gross; T.ot += ot; T.pt += pt; T.pf += pf; T.esic += es; T.adv += adv; T.net += net;
          items.push({ empId: r.empId || '—', name: r.name || '—', designation: r.designation || '—', gross: gross, ot: ot, pt: pt, pf: pf, esic: es, advance: adv, net: net });
        });
        o.preparedBy = meta.preparedBy; o.checkedBy = meta.checkedBy; o.approvedBy = meta.approvedBy;
      }
      if (!items.length) return { orientation: 'landscape', blocks: [{ t: 'header', title: 'PAYROLL — SALARY REGISTER', sub: 'Saagar Traders · Latur', period: period }, { t: 'empty', text: 'No employees in payroll.' }] };
      items.forEach(function (r, i) {
        body.push([String(i + 1), r.empId, trunc(r.name, 22), trunc(r.designation, 16), inr(r.gross), inr(r.ot), inr(r.pt), inr(r.pf), inr(r.esic), inr(r.advance), inr(r.net)]);
        raw.push([0, 0, 0, 0, r.gross, r.ot, r.pt, r.pf, r.esic, r.advance, r.net]); // exact per-row numerics → Σraw == foot
      });
      var blocks = [{ t: 'header', title: 'PAYROLL — SALARY REGISTER', sub: 'Saagar Traders · Latur', period: period, chip: locked ? 'LOCKED — FINAL' : 'DRAFT', chipKind: locked ? 'locked' : 'draft' }];
      blocks.push({ t: 'kpi', cols: 5, items: [
        { label: 'Employees', value: num(items.length) },
        { label: 'Gross + OT', value: inr(T.gross + T.ot) },
        { label: 'Total Deductions', value: inr(T.pt + T.pf + T.esic + T.adv) },
        { label: 'Total Advance', value: inr(T.adv) },
        { label: 'Net Payable', value: inr(T.net), hero: true }
      ] });
      blocks.push({ t: 'table',
        head: [['Sr', 'Emp ID', 'Employee', 'Designation', 'Gross', 'OT', 'PT', 'PF (EE)', 'ESIC', 'Advance', 'Net Pay']],
        body: body, raw: raw, money: [4, 5, 6, 7, 8, 9, 10],
        colStyles: { 0: { cellWidth: 24, halign: 'center' }, 1: { cellWidth: 56 }, 2: { cellWidth: 'auto' }, 3: { cellWidth: 92 }, 4: { cellWidth: 78, halign: 'right' }, 5: { cellWidth: 56, halign: 'right' }, 6: { cellWidth: 54, halign: 'right' }, 7: { cellWidth: 62, halign: 'right' }, 8: { cellWidth: 60, halign: 'right' }, 9: { cellWidth: 70, halign: 'right' }, 10: { cellWidth: 86, halign: 'right' } },
        foot: [['', '', 'TOTAL (' + items.length + ')', '', inr(T.gross), inr(T.ot), inr(T.pt), inr(T.pf), inr(T.esic), inr(T.adv), inr(T.net)]] });
      blocks.push({ t: 'statline', spans: [['Statutory — PT', inr(T.pt)], ['PF (EE)', inr(T.pf)], ['ESIC (EE)', inr(T.esic)], ['Net paid to staff', inr(T.net)]] });
      blocks.push({ t: 'sign', boxes: [{ role: 'Prepared By', name: o.preparedBy || '—' }, { role: 'Checked By', name: o.checkedBy || '—' }, { role: 'For Saagar Traders', name: o.approvedBy || '—' }] });
      return { orientation: 'landscape', blocks: blocks };
    },

    /* ===== PAYROLL — SALARY SLIP (portrait, per employee) ===== */
    payrollSlip: function (o) {
      o = o || {};
      if (o.slip) return { orientation: 'portrait', blocks: [{ t: 'header', title: 'SALARY SLIP', sub: 'Saagar Traders · Latur', period: 'For ' + (o.slip.period || '') }].concat(slipBlocks(o.slip, 'Employee & Attendance')) };
      var p = J('payroll_suite_v1_2026', {}) || {}, meta = p.meta || {}, rows = Array.isArray(p.rows) ? p.rows : [];
      var r = o.empId ? rows.filter(function (x) { return x.empId === o.empId; })[0] : (o.empIndex != null ? rows[o.empIndex] : rows[0]);
      var period = (meta.month || '') + ' ' + (meta.year || '');
      var hdr = { t: 'header', title: 'SALARY SLIP', sub: 'Saagar Traders · Latur', period: 'For ' + period };
      if (!r) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'Employee not found.' }] };
      var gross = Number(r.grossPayable != null ? r.grossPayable : (r.gross || 0)) || 0, pt = Number(r.pt) || 0, pf = Number(r.pf) || 0, es = Number(r.esic) || 0, adv = Number(r.advance) || 0, net = Number(r.net != null ? r.net : 0) || 0, ded = pt + pf + es + adv, ot = Math.max(0, Math.round(net + ded - gross));
      var blocks = [hdr];
      blocks.push({ t: 'section', title: 'Employee & Attendance' });
      blocks.push({ t: 'kv', cols: 2, pairs: [
        ['Employee', r.name || '—'], ['Employee ID', r.empId || '—'], ['Designation', r.designation || '—'], ['Pay Period', period],
        ['Bank', r.bankName || '—'], ['A/C No.', r.accountNo || '—'], ['IFSC', r.ifsc || '—'], ['Payment Mode', r.paidMode || r.payMode || '—'],
        ['Days absent', num(r.absent || 0)], ['Half / Leave days', num(r.halfDay || 0) + ' / ' + num(r.leavesApplied || 0)]
      ] });
      blocks.push({ t: 'section', title: 'Earnings', tag: { cls: 'ok', txt: inr(gross + ot) } });
      blocks.push({ t: 'kv', cols: 2, pairs: [['Gross Salary (payable)', inr(gross)], ['Overtime', inr(ot)], ['Total Earnings', inr(gross + ot), 'big']] });
      blocks.push({ t: 'section', title: 'Deductions', tag: { cls: 'warn', txt: inr(ded) } });
      blocks.push({ t: 'kv', cols: 2, pairs: [['Professional Tax', inr(pt)], ['PF (Employee)', inr(pf)], ['ESIC (Employee)', inr(es)], ['Advance', inr(adv)], ['Total Deductions', inr(ded), 'big']] });
      blocks.push({ t: 'netbox', label: 'Net Pay (Take Home)', value: inr(net) });
      if (r.salaryRemark) blocks.push({ t: 'note', text: 'Remark: ' + r.salaryRemark });
      if (r.paidRef) blocks.push({ t: 'note', text: 'Payment: ' + (r.paidMode || '') + ' · Ref ' + r.paidRef + (r.paidDate ? ' · ' + r.paidDate : '') });
      blocks.push({ t: 'sign', boxes: [{ role: 'Employee', name: r.name || '' }, { role: 'For Saagar Traders', name: meta.approvedBy || 'Authorised' }] });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== PAYROLL — STATUTORY SUMMARY (portrait) =====
       Figures come live from the payroll module via opts.totals (employer PF/ESIC are NOT mirrored to
       localStorage, and rounding each side independently means they can't be derived — so they must be
       passed in). Falls back to a clear "open the module" note when called with no totals. */
    statutorySummary: function (o) {
      o = o || {};
      var t = o.totals || null;
      var period = o.period || '';
      if (!t) { // P1-48: pack/hub path — fall back to the statTotals frozen by payroll lockRun()
        var fb = _payrollLockedTotals(o.month);
        if (fb) { t = fb.totals; period = period || fb.period; o.preparedBy = o.preparedBy || fb.preparedBy; o.approvedBy = o.approvedBy || fb.approvedBy; }
      }
      var hdr = { t: 'header', title: 'STATUTORY SUMMARY', sub: 'Saagar Traders · Latur', period: period };
      if (!t) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'Open the Payroll module → Reports tab to generate the statutory summary.' }] };
      var pfEE = Number(t.pfEE) || 0, pfER = Number(t.pfER) || 0, esEE = Number(t.esEE) || 0, esER = Number(t.esER) || 0, pt = Number(t.pt) || 0, net = Number(t.net) || 0, emp = Number(t.emp) || 0;
      var totEE = pfEE + esEE + pt, totER = pfER + esER, grand = totEE + totER;
      var blocks = [hdr];
      blocks.push({ t: 'kpi', cols: 4, items: [
        { label: 'Employees', value: num(emp) },
        { label: 'Employee Deductions', value: inr(totEE) },
        { label: 'Employer Contribution', value: inr(totER) },
        { label: 'Total Statutory', value: inr(grand), hero: true }
      ] });
      blocks.push({ t: 'section', title: 'Contributions — PF / ESIC / PT' });
      blocks.push({ t: 'table',
        head: [['Component', 'Employee', 'Employer', 'Total']],
        body: [
          ['Provident Fund (PF)', inr(pfEE), inr(pfER), inr(pfEE + pfER)],
          ['ESIC', inr(esEE), inr(esER), inr(esEE + esER)],
          ['Professional Tax (PT)', inr(pt), inr(0), inr(pt)]
        ],
        raw: [[0, pfEE, pfER, pfEE + pfER], [0, esEE, esER, esEE + esER], [0, pt, 0, pt]], money: [1, 2, 3],
        colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 120, halign: 'right' }, 2: { cellWidth: 120, halign: 'right' }, 3: { cellWidth: 120, halign: 'right' } },
        foot: [['TOTAL', inr(totEE), inr(totER), inr(grand)]] });
      blocks.push({ t: 'statline', spans: [['Net paid to staff', inr(net)], ['Total remittance (PF+ESIC+PT)', inr(grand)]] });
      blocks.push({ t: 'note', text: 'Employer PF/ESIC are the exact per-employee totals from the payroll sheet. Verify against your challans before remittance.' });
      blocks.push({ t: 'sign', boxes: [{ role: 'Prepared By', name: o.preparedBy || '—' }, { role: 'For Saagar Traders', name: o.approvedBy || 'Authorised Signatory' }] });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== PAYROLL — STATUTORY REGISTER, MEMBER-WISE (landscape) — Wave 11 P1-26 =====
       Live-opts-only like statutorySummary: employer PF/ESIC and UAN-joined figures are computed in
       the payroll module and passed via opts.rows; no localStorage fallback is possible. */
    statutoryRegister: function (o) {
      o = o || {};
      var hdr = { t: 'header', title: 'STATUTORY REGISTER — MEMBER-WISE', sub: 'Saagar Traders · Latur', period: o.period || '',
        chip: o.locked ? 'LOCKED — FINAL' : 'DRAFT', chipKind: o.locked ? 'locked' : 'draft' };
      if (!Array.isArray(o.rows) || !o.rows.length)
        return { orientation: 'landscape', blocks: [hdr, { t: 'empty', text: 'Open the Payroll module → Reports tab to generate the member-wise statutory register.' }] };
      var T = o.totals || {}, body = [], raw = [];
      o.rows.forEach(function (r, i) {
        body.push([String(i + 1), r.uan || '—', trunc(r.name || '—', 20), inr(r.pfWages), inr(r.pfEE), inr(r.pfER),
          r.esicIp || '—', inr(r.esicWages), inr(r.esicEE), inr(r.esicER), inr(r.pt)]);
        raw.push([0, 0, 0, Number(r.pfWages) || 0, Number(r.pfEE) || 0, Number(r.pfER) || 0, 0,
          Number(r.esicWages) || 0, Number(r.esicEE) || 0, Number(r.esicER) || 0, Number(r.pt) || 0]);
      });
      var blocks = [hdr];
      blocks.push({ t: 'kpi', cols: 4, items: [
        { label: 'Employees', value: num(o.rows.length) },
        { label: 'PF (EE + ER)', value: inr((Number(T.pfEE) || 0) + (Number(T.pfER) || 0)) },
        { label: 'ESIC (EE + ER)', value: inr((Number(T.esicEE) || 0) + (Number(T.esicER) || 0)) },
        { label: 'Prof. Tax', value: inr(Number(T.pt) || 0), hero: true } ] });
      blocks.push({ t: 'table',
        head: [['Sr', 'UAN', 'Employee', 'PF Wages', 'PF EE', 'PF ER', 'ESIC IP No', 'ESIC Wages', 'ESIC EE', 'ESIC ER', 'PT']],
        body: body, raw: raw, money: [3, 4, 5, 7, 8, 9, 10],
        colStyles: { 0: { cellWidth: 24, halign: 'center' }, 1: { cellWidth: 84 }, 2: { cellWidth: 'auto' }, 3: { cellWidth: 62, halign: 'right' }, 4: { cellWidth: 54, halign: 'right' }, 5: { cellWidth: 54, halign: 'right' }, 6: { cellWidth: 84 }, 7: { cellWidth: 66, halign: 'right' }, 8: { cellWidth: 56, halign: 'right' }, 9: { cellWidth: 56, halign: 'right' }, 10: { cellWidth: 50, halign: 'right' } },
        foot: [['', '', 'TOTAL (' + o.rows.length + ')', inr(T.pfWages), inr(T.pfEE), inr(T.pfER), '', inr(T.esicWages), inr(T.esicEE), inr(T.esicER), inr(T.pt)]] });
      blocks.push({ t: 'note', text: 'PF columns follow the ECR keying order (UAN → wages → EE → ER). Missing UAN / ESIC IP numbers print as "—" — fill them in Employee Master before filing.' });
      blocks.push({ t: 'sign', boxes: [{ role: 'Prepared By', name: o.preparedBy || '—' }, { role: 'For Saagar Traders', name: o.approvedBy || 'Authorised Signatory' }] });
      return { orientation: 'landscape', blocks: blocks };
    },

    /* ===== QMS — END OF DAY OWNER SUMMARY (portrait) =====
       Figures come live from the QMS module via opts (same calcStats()/croPerfRows() that paint the
       Dashboard & Reports on screen) — payroll statutorySummary pattern. Falls back to a clear
       "open the module" note when called with no data (e.g. re-opened from report history). */
    qmsEodSummary: function (o) {
      o = o || {};
      var date = o.date || curDate();
      var hdr = { t: 'header', title: 'QMS — END OF DAY SUMMARY', sub: (o.store || 'Saagar Traders') + ' · Latur', period: longDate(date), chip: o.closedAt ? 'EOD CLOSED' : 'DAY OPEN', chipKind: o.closedAt ? 'locked' : 'draft' };
      var k = o.kpi || null;
      if (!k) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'Open the QMS module → Reports → Share Day Summary to generate this report.' }] };
      var lost = Array.isArray(o.lostReasons) ? o.lostReasons : [];
      var topReason = lost[0] ? (lost[0].reason + ' (' + lost[0].count + ')') : 'None';
      var blocks = [hdr, { t: 'kpi', cols: 5, items: [
        { label: 'Walk-ins', value: num(k.walkins) },
        { label: 'Purchases', value: num(k.purchases), sub: num(k.service) + ' service · ' + num(k.non) + ' non-purchase' },
        { label: 'Conversion', value: pct(k.conv), hero: true },
        { label: 'Sales', value: inr(k.sales) },
        { label: 'Lost', value: inr(k.lost), sub: trunc(topReason, 26), subClass: (Number(k.lost) || 0) > 0 ? 'down' : 'up' }
      ] }];
      var cros = Array.isArray(o.cros) ? o.cros : [];
      blocks.push({ t: 'section', title: 'CRO Performance — turns, conversion, sales, skips' });
      if (cros.length) {
        var T = cros.reduce(function (a, r) { a.turns += Number(r.turns) || 0; a.assigned += Number(r.assigned) || 0; a.pur += Number(r.purchases) || 0; a.sales += Number(r.sales) || 0; a.skips += Number(r.skips) || 0; return a; }, { turns: 0, assigned: 0, pur: 0, sales: 0, skips: 0 });
        blocks.push({ t: 'table',
          head: [['CRO', 'Turns', 'Assigned', 'Purchases', 'Conv %', 'Sales ₹', 'Skips']],
          body: cros.map(function (r) { return [trunc(r.name, 22), num(r.turns), num(r.assigned), num(r.purchases), (Number(r.assigned) ? (Number(r.conv) || 0) + '%' : '—'), inr(r.sales), num(r.skips)]; }),
          money: [5],
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 62, halign: 'right' }, 2: { cellWidth: 76, halign: 'right' }, 3: { cellWidth: 82, halign: 'right' }, 4: { cellWidth: 66, halign: 'right' }, 5: { cellWidth: 100, halign: 'right' }, 6: { cellWidth: 58, halign: 'right' } },
          foot: [['Total', num(T.turns), num(T.assigned), num(T.pur), (T.assigned ? Math.round(T.pur / T.assigned * 100) + '%' : '—'), inr(T.sales), num(T.skips)]] });
      } else { blocks.push({ t: 'empty', text: 'No CROs on this day\'s rotation.' }); }
      blocks.push({ t: 'section', title: 'Lost Sales — by reason', tag: lost.length ? { cls: 'warn', txt: inr(k.lost) } : { cls: 'ok', txt: '0' } });
      if (lost.length) {
        blocks.push({ t: 'table',
          head: [['Reason', 'Count', 'Est. Lost ₹']],
          body: lost.map(function (x) { return [trunc(x.reason, 48), num(x.count), inr(x.value)]; }),
          money: [2],
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 90, halign: 'right' }, 2: { cellWidth: 150, halign: 'right' } },
          foot: [['Total', num(k.non), inr(k.lost)]] });
      } else { blocks.push({ t: 'empty', text: 'No lost-sale reasons logged.' }); }
      if (o.closedAt) blocks.push({ t: 'note', text: 'EOD closed ' + (function () { try { return new Date(o.closedAt).toLocaleString('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }); } catch (e) { return String(o.closedAt); } })() + (o.closedBy ? ' by ' + o.closedBy : '') + ' · Generated ' + stamp() });
      else blocks.push({ t: 'note', text: 'Day not yet EOD-closed when this summary was generated · ' + stamp() });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== PAYROLL — ADVANCE VOUCHER (portrait) — data passed live from the module ===== */
    advanceVoucher: function (o) {
      o = o || {};
      var v = o.voucher || null;
      var hdr = { t: 'header', title: 'ADVANCE VOUCHER', sub: 'Saagar Traders · Latur', period: (v && v.date) ? ('Date ' + v.date) : '' };
      if (!v) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'No voucher data — open the Payroll module → Advances.' }] };
      var blocks = [hdr, { t: 'section', title: 'Salary Advance — Payment Receipt' }];
      blocks.push({ t: 'kv', cols: 2, pairs: [
        ['Voucher No.', v.voucherNo || '—'], ['Date', v.date || '—'],
        ['Employee', v.empName || '—'], ['Employee ID', v.empId || '—'],
        ['Advance Amount', inr(v.amount)], ['Recovery From', ((v.deductMonth || '') + ' ' + (v.deductYear || '')).trim() || '—'],
        ['Recovery Plan', v.recoveryPlan || '—'], ['Outstanding', inr(v.outstanding)],
        ['Mode of Payment', v.mode || 'Cash'], ['Reference No.', v.refNo || '—'],
        ['Reason', v.reason || '—'], ['Approved By', v.approvedBy || '—']
      ] });
      blocks.push({ t: 'netbox', label: 'Advance Paid', value: inr(v.amount) });
      blocks.push({ t: 'note', text: 'In words: Rupees ' + (v.amountWords || '') + ' Only.' });
      blocks.push({ t: 'para', text: 'I acknowledge receipt of the above advance from Saagar Traders and authorise its deduction from my salary for ' + ((v.deductMonth || '') + ' ' + (v.deductYear || '')).trim() + '.' });
      blocks.push({ t: 'sign', boxes: [{ role: 'Employee Signature', name: v.empName || '' }, { role: 'Approved / For Saagar Traders', name: v.approvedBy || 'Authorised Signatory' }] });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== PAYROLL — HR LETTER (portrait) — prose paragraphs passed live from the module ===== */
    hrLetter: function (o) {
      o = o || {};
      var L = o.letter || null;
      var hdr = { t: 'header', title: (L && L.type) ? String(L.type).toUpperCase() : 'HR LETTER', sub: 'Saagar Traders · Latur', period: (L && L.dateNice) ? ('Date ' + L.dateNice) : '' };
      if (!L) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'No letter data — open the Payroll module → HR Letters.' }] };
      var blocks = [hdr];
      if (L.ref) blocks.push({ t: 'note', text: 'Ref: ' + L.ref });
      (L.paras || []).forEach(function (p) { if (p) blocks.push({ t: 'para', text: p }); });
      blocks.push({ t: 'para', text: L.closing || 'Yours sincerely,' });
      blocks.push({ t: 'sign', boxes: [{ role: 'For Saagar Traders', name: 'Authorised Signatory' }, { role: 'Employee Acknowledgement', name: L.name || '' }] });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== PAYROLL — FULL & FINAL SETTLEMENT (portrait) — Wave 11 P1-27 =====
       Record assembled and stored by the payroll module (payroll_fnf_v1) and passed live via
       opts.fnf — re-prints use the stored numbers verbatim, never recomputed here. */
    fnfSettlement: function (o) {
      var F = (o || {}).fnf || null;
      var hdr = { t: 'header', title: 'FULL & FINAL SETTLEMENT', sub: 'Saagar Traders · Latur', period: F ? (F.period || '') : '' };
      if (!F) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'No settlement data — open Payroll → Letters → Full & Final Settlement.' }] };
      var blocks = [hdr];
      if (F.ref) blocks.push({ t: 'note', text: 'Ref: ' + F.ref + (F.generatedAt ? ' · Generated ' + String(F.generatedAt).slice(0, 10) : '') + (F.generatedBy ? ' · By ' + F.generatedBy : '') });
      blocks.push({ t: 'kv', cols: 2, pairs: [
        ['Employee', F.name || '—'], ['Employee ID', F.empId || '—'],
        ['Designation', F.designation || '—'], ['Final Pay Period', F.period || '—'],
        ['Last Working Day', F.lastDay || '—'], ['Settlement Status', 'Full & Final'] ] });
      var E = Array.isArray(F.earnings) ? F.earnings : [], R = Array.isArray(F.recoveries) ? F.recoveries : [];
      var n = Math.max(E.length, R.length), body = [], raw = [];
      for (var i = 0; i < n; i++) { var e = E[i] || {}, r = R[i] || {};
        body.push([e.label ? trunc(e.label, 46) : '', e.label ? inr(e.amt) : '', r.label ? trunc(r.label, 46) : '', r.label ? inr(r.amt) : '']);
        raw.push([0, e.amt || 0, 0, r.amt || 0]); }
      blocks.push({ t: 'table', head: [['Earnings / Dues', 'Amount (₹)', 'Recoveries / Deductions', 'Amount (₹)']],
        body: body, raw: raw, money: [1, 3],
        colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 96, halign: 'right' }, 2: { cellWidth: 'auto' }, 3: { cellWidth: 96, halign: 'right' } },
        foot: [['Total Dues', inr(F.totEarn), 'Total Recoveries', inr(F.totRec)]] });
      var neg = Number(F.net) < 0;
      blocks.push({ t: 'netbox', label: neg ? 'Net Recoverable from Employee' : 'Net Payable on Settlement', value: inr(Math.abs(Number(F.net) || 0)) });
      blocks.push({ t: 'note', text: 'In words: Rupees ' + (F.netWords || '') + ' Only' + (neg ? ' (recoverable)' : '') + '.' });
      blocks.push({ t: 'para', text: 'This Full & Final Settlement is prepared in respect of the above employee upon cessation of employment with Saagar Traders, Latur. On acceptance of the net amount stated above, the employee confirms that no further dues remain payable by either party, save as required by law.' });
      blocks.push({ t: 'sign', boxes: [{ role: 'Employee (Received & Accepted)', name: F.name || '' }, { role: 'For Saagar Traders (Owner)', name: 'Authorised Signatory' }] });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== LEAVE REGISTER (landscape, month) ===== */
    leaveRegister: function (o) {
      var month = o.month || curMonth();
      var d = J('leavedesk_v3', {}) || {}, by = (d && d.leaves) || {}, emps = Array.isArray(d.employees) ? d.employees : [];
      var empMap = {}; emps.forEach(function (e) { empMap[(e.name || '').toLowerCase()] = e; });
      /* Audit fix bug-01: inclusive local-midnight day span between two ISO dates (floor, min 1). */
      function leaveDaySpan(fromISO, toISO){ try{ var a=String(fromISO).slice(0,10).split('-'), b=String(toISO).slice(0,10).split('-'); if(a.length!==3||b.length!==3) return 1; var da=new Date(+a[0],+a[1]-1,+a[2]), db=new Date(+b[0],+b[1]-1,+b[2]); var n=Math.floor((db-da)/86400000)+1; return n>=1?n:1; }catch(e){ return 1; } }
      var seen = {}, out = [];
      Object.keys(by).filter(function (dk) { return dk.slice(0, 7) === month; }).sort().forEach(function (dk) {
        var arr = by[dk]; if (!Array.isArray(arr)) return;
        arr.forEach(function (l) { if (!l || !l.name) return; if (l.status && l.status !== 'approved') return; /* pending/rejected excluded — mirrors the bridge LEAVE_APPROVED guard; legacy status-less rows count as approved */ var key = (l.name + '|' + (l.leaveFrom || dk) + '|' + (l.leaveTo || dk) + '|' + (l.type || '')).toLowerCase(); if (seen[key]) { if (!seen[key]._spanned) seen[key].days++; return; } var lt = l.type === 'half_day_am' ? 'Half (AM)' : l.type === 'half_day_pm' ? 'Half (PM)' : 'Full day'; var __span = (l.leaveFrom && l.leaveTo && l.leaveFrom !== l.leaveTo) ? leaveDaySpan(l.leaveFrom, l.leaveTo) : 1; var rec = { name: l.name, type: lt, half: /half/i.test(lt), from: l.leaveFrom || dk, to: l.leaveTo || dk, category: l.category || '', reason: l.reason || '', approvedBy: l.approvedBy || '', days: __span, _spanned: __span > 1 }; seen[key] = rec; out.push(rec); });
      });
      var hdr = { t: 'header', title: 'LEAVE REGISTER', sub: 'Saagar Traders · Latur', period: monthLong(month) };
      if (!out.length) return { orientation: 'landscape', blocks: [hdr, { t: 'empty', text: 'No leave recorded for this month.' }] };
      var totDays = 0, full = 0, half = 0, names = {}, body = [], raw = [];
      out.forEach(function (r, i) { var ld = r.half ? r.days * 0.5 : r.days; totDays += ld; if (r.half) half++; else full++; names[r.name.toLowerCase()] = 1; var e = empMap[r.name.toLowerCase()] || {};
        body.push([String(i + 1), e.empId || '—', trunc(r.name, 20), trunc(e.department || '—', 12), r.from, r.to, r.type, num(r.days), fmtLD(ld), trunc(r.category || '—', 14), trunc(r.reason || '—', 26), trunc(r.approvedBy || '—', 16)]);
        raw.push([0, 0, 0, 0, 0, 0, 0, 0, ld, 0, 0, 0]); // leave-days fractional → float-exact carry
      });
      var blocks = [hdr, { t: 'kpi', cols: 5, items: [
        { label: 'Applications', value: num(out.length) },
        { label: 'Leave Days (payroll)', value: fmtLD(totDays), hero: true },
        { label: 'Full-day', value: num(full) },
        { label: 'Half-day', value: num(half) },
        { label: 'Employees', value: num(Object.keys(names).length) }
      ] }];
      blocks.push({ t: 'table',
        head: [['Sr', 'Emp ID', 'Employee', 'Dept', 'From', 'To', 'Type', 'Days', 'Leave-Days', 'Category', 'Reason', 'Appr. By']],
        body: body, raw: raw, money: [8], fmt: { 8: fmtLD },
        colStyles: { 0: { cellWidth: 22, halign: 'center' }, 1: { cellWidth: 52 }, 2: { cellWidth: 'auto' }, 3: { cellWidth: 64 }, 4: { cellWidth: 64 }, 5: { cellWidth: 64 }, 6: { cellWidth: 58 }, 7: { cellWidth: 40, halign: 'right' }, 8: { cellWidth: 64, halign: 'right' }, 9: { cellWidth: 74 }, 10: { cellWidth: 'auto' }, 11: { cellWidth: 80 } },
        foot: [['', '', 'TOTAL (' + out.length + ')', '', '', '', '', '', fmtLD(totDays), '', '', '']] });
      return { orientation: 'landscape', blocks: blocks };
    },

    /* ===== GROOMING — DAILY AUDIT (portrait) ===== */
    groomingDaily: function (o) {
      var date = o.date || curDate(); var g = G('computeGroomingDay', function () { return { checks: 0, pass: 0, avg: 0, list: [] }; })(date);
      var arr = J('saagar_grooming_' + date, []) || [];
      var hdr = { t: 'header', title: 'DAILY GROOMING AUDIT', sub: 'Titan World + Helios · Latur', period: longDate(date) };
      if (!g.checks) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'No grooming audit recorded for this date.' }] };
      var perfect = arr.filter(function (r) { return Number(r.pct) === 100; }).length, below = arr.filter(function (r) { return (Number(r.pct) || 0) < 80; }).length;
      var blocks = [hdr, { t: 'kpi', cols: 5, items: [
        { label: 'CROs Checked', value: num(g.checks) },
        { label: 'Passed >=80%', value: num(g.pass), subClass: 'up' },
        { label: 'Below 80%', value: num(below), subClass: below ? 'down' : 'up' },
        { label: 'Perfect 100%', value: num(perfect) },
        { label: 'Day Average', value: g.avg + '%', hero: true }
      ] }];
      var rows = arr.slice().sort(function (a, b) { return (Number(a.pct) || 0) - (Number(b.pct) || 0); });
      function failedLabels(r) { return (Array.isArray(r.items) ? r.items.filter(function (i) { return i && !i.passed; }).map(function (i) { return i.label; }) : []).filter(Boolean); }
      function failCount(r) { return Array.isArray(r.items) ? failedLabels(r).length : Math.max(0, (Number(r.total) || 0) - (Number(r.checked) || 0)); }
      /* 1) at-a-glance summary — one row per CRO (worst first); 'Failed' is the exact count, expanded in full below */
      blocks.push({ t: 'section', title: 'CRO Grooming Summary', tag: below ? { cls: 'bad', txt: below + ' below 80%' } : { cls: 'ok', txt: 'All >=80%' } });
      blocks.push({ t: 'table',
        head: [['CRO', 'Gender', 'Score', 'Passed', 'Failed', 'Result']],
        body: rows.map(function (r) { var pass = (Number(r.pct) || 0) >= 80; return [trunc(r.name || '—', 26), r.gender === 'f' ? 'Female' : 'Male', (Number(r.pct) || 0) + '%', (r.checked != null ? r.checked : '—') + ' / ' + (r.total != null ? r.total : '—'), String(failCount(r)), pass ? 'Pass' : 'Fail']; }),
        flagRows: rows.map(function (r) { return (Number(r.pct) || 0) < 80; }),
        pills: [5],
        colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 66 }, 2: { cellWidth: 52, halign: 'right' }, 3: { cellWidth: 70, halign: 'center' }, 4: { cellWidth: 54, halign: 'center' }, 5: { cellWidth: 76, halign: 'center' } } });
      /* 2) failures-only detail — ONLY CROs with >=1 missed parameter (worst score first); clean CROs are already in the summary above */
      blocks.push({ t: 'section', title: 'Failures Detail' });
      var failing = rows.filter(function (r) { return failCount(r) > 0; });
      if (!failing.length) {
        blocks.push({ t: 'note', text: 'All CROs passed every grooming parameter — nothing to action today.' });
      } else {
        failing.forEach(function (r) {
          var pct = (Number(r.pct) || 0), pass = pct >= 80, fc = failCount(r), fails = failedLabels(r);
          /* tag reflects the REAL pass/fail state: a CRO can score >=80% (Pass) yet still miss a few items */
          blocks.push({ t: 'section', title: r.name || '—', tag: pass ? { cls: 'warn', txt: pct + '% · ' + fc + ' to fix' } : { cls: 'bad', txt: pct + '% BELOW 80%' } });
          blocks.push({ t: 'statline', spans: [['Gender', r.gender === 'f' ? 'Female' : 'Male'], ['Score', pct + '%'], ['Passed', (r.checked != null ? r.checked : '—') + ' / ' + (r.total != null ? r.total : '—')], ['Failed', String(fc)], ['Audited', r.time || '—']] });
          if (fails.length) {
            blocks.push({ t: 'table',
              head: [['#', 'Failed Parameter']],
              body: fails.map(function (label, i) { return [String(i + 1), label]; }),
              flagRows: fails.map(function () { return true; }),
              colStyles: { 0: { cellWidth: 26, halign: 'center' }, 1: { cellWidth: 'auto' } } });
          } else {
            blocks.push({ t: 'note', text: 'Per-parameter breakdown not stored — ' + fc + ' parameter(s) failed.' });
          }
        });
      }
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== GROOMING — MONTHLY TREND (portrait) ===== */
    groomingMonthly: function (o) {
      var month = o.month || curMonth(); var gm = G('computeGroomingMonth', function () { return { days: [], totalChecks: 0, avg: 0, daysWithData: 0 }; })(month);
      var cm = {};
      for (var i = 0; i < localStorage.length; i++) { var k = localStorage.key(i); if (k && k.indexOf('saagar_grooming_' + month) === 0) { var arr = J(k, []); (Array.isArray(arr) ? arr : []).forEach(function (r) { var nm = (r.name || '').trim(); if (!nm) return; var key = nm.toLowerCase(); if (!cm[key]) cm[key] = { name: nm, gender: r.gender, sum: 0, n: 0, best: 0, low: 100, pass: 0 }; var pc = Number(r.pct) || 0; cm[key].sum += pc; cm[key].n++; cm[key].best = Math.max(cm[key].best, pc); cm[key].low = Math.min(cm[key].low, pc); if (pc >= 80) cm[key].pass++; }); } }
      var cros = Object.keys(cm).map(function (k) { var c = cm[k]; return { name: c.name, gender: c.gender, n: c.n, avg: Math.round(c.sum / c.n), best: c.best, low: c.low, cons: Math.round(c.pass / c.n * 100) }; }).sort(function (a, b) { return b.avg - a.avg; });
      var hdr = { t: 'header', title: 'MONTHLY GROOMING TREND', sub: 'Saagar Traders · Latur', period: monthLong(month) };
      if (!cros.length) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'No grooming audits recorded for this month.' }] };
      var best = cros[0];
      var blocks = [hdr, { t: 'kpi', cols: 5, items: [
        { label: 'CROs', value: num(cros.length) },
        { label: 'Check-ins', value: num(gm.totalChecks) },
        { label: 'Month Average', value: gm.avg + '%', hero: true },
        { label: 'Best CRO', value: best.name, sub: best.avg + '%', subClass: 'up' },
        { label: 'Days Audited', value: num(gm.daysWithData) }
      ] }];
      blocks.push({ t: 'section', title: 'CRO Leaderboard' });
      blocks.push({ t: 'table',
        head: [['Rank', 'CRO', 'G', 'Check-ins', 'Avg', 'Best', 'Low', 'Consistency']],
        body: cros.map(function (c, i) { return [String(i + 1), trunc(c.name, 24), c.gender === 'f' ? 'F' : 'M', num(c.n), c.avg + '%', c.best + '%', c.low + '%', c.cons + '%']; }),
        flagRows: cros.map(function (c) { return c.avg < 80; }),
        colStyles: { 0: { cellWidth: 40, halign: 'center' }, 1: { cellWidth: 'auto' }, 2: { cellWidth: 30, halign: 'center' }, 3: { cellWidth: 72, halign: 'right' }, 4: { cellWidth: 56, halign: 'right' }, 5: { cellWidth: 56, halign: 'right' }, 6: { cellWidth: 56, halign: 'right' }, 7: { cellWidth: 86, halign: 'right' } } });
      blocks.push({ t: 'note', text: 'Month average ' + gm.avg + '% across ' + gm.totalChecks + ' check-ins · ' + gm.daysWithData + ' days audited.' });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== EXPENSE — MONTHLY P&L SUMMARY (portrait) ===== */
    expenseMonthly: function (o) {
      var month = o.month || curMonth(); var e = expenseMonth(month);
      var hdr = { t: 'header', title: 'MONTHLY EXPENSE & P&L', sub: 'Saagar Traders · Latur', period: monthLong(month) };
      if (!e.incTot && !e.expTot) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'No ledger entries for this month.' }] };
      var topExp = ''; var mv = 0; Object.keys(e.exp).forEach(function (k) { if (e.exp[k] > mv) { mv = e.exp[k]; topExp = k; } });
      var blocks = [hdr, { t: 'kpi', cols: 5, items: [
        { label: 'Total Income', value: inr(e.incTot) },
        { label: 'Total Expense', value: inr(e.expTot) },
        { label: 'Net P&L', value: inr(e.net), hero: true, subClass: e.net >= 0 ? 'up' : 'down', sub: e.net >= 0 ? 'profit' : 'loss' },
        { label: 'Net Margin', value: e.incTot ? Math.round(e.net / e.incTot * 100) + '%' : '—' },
        { label: 'Top Expense', value: topExp || '—' }
      ] }];
      function catTable(title, map, tot, tag) {
        blocks.push({ t: 'section', title: title, tag: tag });
        var arr = Object.keys(map).map(function (k) { return { k: k, v: map[k] }; }).sort(function (a, b) { return b.v - a.v; });
        if (!arr.length) { blocks.push({ t: 'empty', text: 'None.' }); return; }
        blocks.push({ t: 'table',
          head: [['Category', 'Amount ₹', '%']],
          body: arr.map(function (x) { return [trunc(x.k, 36), inr(x.v), tot ? Math.round(x.v / tot * 100) + '%' : '—']; }),
          money: [1],
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 130, halign: 'right' }, 2: { cellWidth: 70, halign: 'right' } },
          foot: [['Total', inr(tot), '']] });
      }
      catTable('Income by Category', e.inc, e.incTot, { cls: 'ok', txt: inr(e.incTot) });
      catTable('Expense by Category', e.exp, e.expTot, { cls: 'warn', txt: inr(e.expTot) });
      blocks.push({ t: 'section', title: 'Expense by Firm / Store' });
      if (e.firms.length) {
        blocks.push({ t: 'table',
          head: [['Firm / Store', 'Entries', 'Expense ₹']],
          body: e.firms.map(function (f) { return [trunc(f.firm, 40), num(f.count), inr(f.amount)]; }),
          money: [2],
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 90, halign: 'right' }, 2: { cellWidth: 130, halign: 'right' } },
          foot: [['Total', '', inr(e.expTot)]] });
      } else { blocks.push({ t: 'empty', text: '—' }); }
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== TAX — COMPLIANCE DUE REPORT (portrait) ===== */
    taxReport: function (o) {
      var t = G('computeTaxStatus', function () { return { overdue: 0, dueWeek: 0, dueMonth: 0, done: 0, upcoming: [] }; })();
      var blocks = [{ t: 'header', title: 'TAX COMPLIANCE — DUE REPORT', sub: 'Saagar Traders · Latur', period: longDate(curDate()) }];
      blocks.push({ t: 'kpi', cols: 5, items: [
        { label: 'Overdue', value: num(t.overdue), hero: true, subClass: t.overdue ? 'down' : 'up', sub: t.overdue ? 'act now' : 'clear' },
        { label: 'Due This Week', value: num(t.dueWeek) },
        { label: 'Due This Month', value: num(t.dueMonth) },
        { label: 'Completed', value: num(t.done), subClass: 'up' },
        { label: 'Upcoming', value: num((t.upcoming || []).length) }
      ] });
      blocks.push({ t: 'section', title: 'Upcoming & Overdue Filings', tag: t.overdue ? { cls: 'bad', txt: t.overdue + ' overdue' } : { cls: 'ok', txt: 'On track' } });
      var rows = (t.upcoming || []);
      if (rows.length) {
        blocks.push({ t: 'table',
          head: [['Firm', 'Compliance Item', 'Due Date', 'Status']],
          body: rows.map(function (u) { return [trunc(u.firm, 22), trunc(u.item, 40), u.due, u.status]; }),
          flagRows: rows.map(function (u) { return u.status === 'Overdue'; }),
          pills: [3],
          colStyles: { 0: { cellWidth: 130 }, 1: { cellWidth: 'auto' }, 2: { cellWidth: 92, halign: 'right' }, 3: { cellWidth: 92, halign: 'center' } } });
      } else { blocks.push({ t: 'empty', text: 'No upcoming compliance items.' }); }
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== SERVICE — OPEN CASES AGING (portrait) ===== */
    serviceAging: function (o) {
      var a = G('computeServiceAging', function () { return { totalOpen: 0, b0_3: 0, b4_7: 0, b8_15: 0, b16: 0 }; })();
      var arr = J('saagar_wsf_v2', []); var closedW = ['delivered', 'closed', 'complete', 'completed', 'cancelled', 'canceled'];
      var open = (Array.isArray(arr) ? arr : []).filter(function (j) { var s = String(j.status || j.stage || '').toLowerCase(); return !closedW.some(function (w) { return s.indexOf(w) >= 0; }); });
      var now = Date.now();
      open.forEach(function (j) { var ds = String(j.bookingDate || j.dateRec || j.date || j.createdAt || '').slice(0, 10); var dt = ds ? new Date(ds + 'T00:00:00') : null; j.__days = dt && !isNaN(+dt) ? Math.max(0, Math.floor((now - +dt) / 86400000)) : 0; j.__date = ds; });   // bug-hunt #2: local-midnight + floor (match computeServiceAging; was Math.round → buckets/SLA flag tripped ~12h early)
      open.sort(function (x, y) { return y.__days - x.__days; });
      var hdr = { t: 'header', title: 'SERVICE — OPEN CASES AGING', sub: 'Watch Service · Latur', period: longDate(curDate()) };
      var blocks = [hdr, { t: 'kpi', cols: 5, items: [
        { label: 'Total Open', value: num(a.totalOpen), hero: true },
        { label: '0–3 days', value: num(a.b0_3) },
        { label: '4–7 days', value: num(a.b4_7) },
        { label: '8–15 days', value: num(a.b8_15) },
        { label: '16+ days', value: num(a.b16), subClass: a.b16 ? 'down' : 'up', sub: a.b16 ? 'overdue' : 'ok' }
      ] }];
      blocks.push({ t: 'section', title: 'Open Cases (oldest first)', tag: a.b16 ? { cls: 'bad', txt: a.b16 + ' aged 16d+' } : { cls: 'ok', txt: 'none aged' } });
      if (!open.length) { blocks.push({ t: 'empty', text: 'No open service cases — all delivered.' }); return { orientation: 'portrait', blocks: blocks }; }
      blocks.push({ t: 'table',
        head: [['Order No', 'Customer', 'Mobile', 'Watch', 'Received', 'Age (days)', 'Status']],
        body: open.map(function (j) { return [j.id || '—', trunc(j.custName || j.customer || j.name || '—', 22), j.custMobile || j.mobile || j.custPhone || j.phone || j.contact || '—', trunc([j.brand, j.model].filter(Boolean).join(' ') || j.item || '—', 22), j.__date || '—', num(j.__days), j.status || 'open']; }),
        flagRows: open.map(function (j) { return j.__days > 15; }),
        pills: [6],
        colStyles: { 0: { cellWidth: 64 }, 1: { cellWidth: 'auto' }, 2: { cellWidth: 84 }, 3: { cellWidth: 'auto' }, 4: { cellWidth: 66, halign: 'right' }, 5: { cellWidth: 56, halign: 'right' }, 6: { cellWidth: 82, halign: 'center' } } });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== SERVICE — JOB CARD (portrait, per case) ===== */
    serviceJobCard: function (o) {
      var arr = J('saagar_wsf_v2', []); arr = Array.isArray(arr) ? arr : [];
      var j = o.id ? arr.filter(function (x) { return x.id === o.id; })[0] : (arr.filter(function (x) { return String(x.status || '').toLowerCase() !== 'closed'; })[0] || arr[0]);
      var hdr = { t: 'header', title: 'WATCH SERVICE — JOB CARD', sub: 'Saagar Traders · Latur', period: longDate(curDate()) };
      if (!j) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'No service cases.' }] };
      var blocks = [{ t: 'header', title: 'WATCH SERVICE — JOB CARD', sub: 'Service Order ' + (j.id || ''), period: longDate(j.dateRec || curDate()) }];
      blocks.push({ t: 'section', title: 'Case' });
      blocks.push({ t: 'kv', cols: 2, pairs: [['Service Order', j.id || '—'], ['Date Received', j.dateRec || '—'], ['Advisor', j.advisor || '—'], ['Expected Delivery', j.expDel || '—'], ['Status', j.status || 'open']] });
      blocks.push({ t: 'section', title: 'Customer' });
      blocks.push({ t: 'kv', cols: 2, pairs: [['Customer', j.custName || '—'], ['Mobile', j.custMobile || '—'], ['Email', j.custEmail || '—']] });
      blocks.push({ t: 'section', title: 'Watch Details' });
      blocks.push({ t: 'kv', cols: 2, pairs: [['Brand', j.brand || '—'], ['Model', j.model || '—'], ['Reference No', j.refNo || '—'], ['Serial No', j.serialNo || '—']] });
      var items = Array.isArray(j.lineItems) ? j.lineItems.filter(function (l) { return l && (l.desc || l.description); }) : [];
      blocks.push({ t: 'section', title: 'Estimate', tag: { cls: 'ok', txt: inr(j.estTotal || 0) } });
      if (items.length) {
        blocks.push({ t: 'table',
          head: [['Description', 'Qty', 'Unit ₹', 'Total ₹']],
          body: items.map(function (l) { return [trunc(l.desc || l.description || '—', 44), num(l.qty || 1), inr(l.unit || 0), inr(l.total || 0)]; }),
          money: [3],
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 50, halign: 'right' }, 2: { cellWidth: 92, halign: 'right' }, 3: { cellWidth: 100, halign: 'right' } },
          foot: [['Total Estimate', '', '', inr(j.estTotal || 0)]] });
      } else { blocks.push({ t: 'kv', cols: 1, pairs: [['Estimated Total', inr(j.estTotal || 0), 'big']] }); }
      if (j.diagnosis) blocks.push({ t: 'note', text: 'Diagnosis: ' + j.diagnosis });
      blocks.push({ t: 'sign', boxes: [{ role: 'Customer Signature', name: j.custName || '' }, { role: 'For Saagar Traders', name: j.advisor || 'Authorised' }] });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== GST TAX INVOICE (portrait, per service case) — Wave 3 =====
       Scope: service-centre & non-franchise sales ONLY (never replaces Titan World POS bills).
       GST rate is the owner-entered per-case value (j.gst) — never invented/hardcoded; a local
       intra-state sale is shown as CGST + SGST (half each). The sequential invoice number is
       stamped by the Service module and passed as o.invoiceNo (falls back to the case id). */
    serviceTaxInvoice: function (o) {
      var arr = J('saagar_wsf_v2', []); arr = Array.isArray(arr) ? arr : [];
      var j = o.id ? arr.filter(function (x) { return x.id === o.id; })[0] : arr[0];
      var hdr0 = { t: 'header', title: 'TAX INVOICE', sub: 'Saagar Traders · Latur', period: longDate(curDate()) };
      if (!j) return { orientation: 'portrait', blocks: [hdr0, { t: 'empty', text: 'No service case selected.' }] };
      var org = J('saagar_org_master_v1', null);
      var firm = (org && Array.isArray(org.firms)) ? (org.firms.filter(function (f) { return f && f.active !== false && f.name; })[0] || org.firms[0]) : null;
      var firmName = (firm && firm.name) || 'Saagar Traders';
      var gstin = (firm && (firm.gstin || firm.gstIn || firm.GSTIN)) || '';
      var firmAddr = (firm && (firm.address || firm.addr)) || 'Latur, Maharashtra';
      var del = j.delivery || {};
      var invNo = o.invoiceNo || j.invoiceNo || ('WS-' + (j.id || ''));
      var invDate = del.collectDate || (j.closedAt ? String(j.closedAt).slice(0, 10) : curDate());
      var sub = parseFloat(j.subTotal) || 0;
      var gstPct = parseFloat(j.gst) || 0;
      var gstAmt = sub * gstPct / 100;
      var finalAmt = parseFloat(del.finalAmt) || (sub + gstAmt);
      var blocks = [{ t: 'header', title: 'TAX INVOICE', sub: firmName + (gstin ? (' · GSTIN ' + gstin) : ''), period: firmAddr, chip: invNo, chipKind: 'locked' }];
      blocks.push({ t: 'kv', cols: 2, pairs: [['Invoice No', invNo], ['Invoice Date', invDate], ['Service Order', j.id || '—'], ['Delivery Date', del.collectDate || '—']] });
      blocks.push({ t: 'section', title: 'Bill To' });
      blocks.push({ t: 'kv', cols: 2, pairs: [['Customer', j.custName || '—'], ['Mobile', j.custMobile || '—'], ['Address', j.custAddr || '—'], ['Watch', [j.brand, j.model].filter(Boolean).join(' ') || '—']] });
      var items = Array.isArray(j.lineItems) ? j.lineItems.filter(function (l) { return l && (l.desc || l.description); }) : [];
      blocks.push({ t: 'section', title: 'Particulars' });
      if (items.length) {
        blocks.push({ t: 'table',
          head: [['Description', 'Qty', 'Rate ₹', 'Amount ₹']],
          body: items.map(function (l) { return [trunc(l.desc || l.description || '—', 46), num(l.qty || 1), inr(l.unit || 0), inr(l.total || 0)]; }),
          money: [3],
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 46, halign: 'right' }, 2: { cellWidth: 90, halign: 'right' }, 3: { cellWidth: 98, halign: 'right' } },
          foot: [['Taxable Value', '', '', inr(sub)]] });
      } else {
        blocks.push({ t: 'table', head: [['Description', 'Amount ₹']], body: [['Service charges', inr(sub)]], money: [1], foot: [['Taxable Value', inr(sub)]] });
      }
      blocks.push({ t: 'section', title: 'Tax Summary' });
      var taxPairs = [];
      /* Audit fix bug-02: inr() rounds to whole rupees, so rounding gstAmt/2 twice independently could make
         CGST+SGST show ₹1 more/less than Total GST. Derive SGST as (roundedTotalGST − roundedCGST) so the
         two halves ALWAYS sum to the displayed Total GST. */
      var totGst = Math.round(gstAmt);
      var cgst = Math.round(gstAmt / 2);
      var sgst = totGst - cgst;
      if (gstPct > 0) { taxPairs.push(['CGST @ ' + (gstPct / 2) + '%', inr(cgst)]); taxPairs.push(['SGST @ ' + (gstPct / 2) + '%', inr(sgst)]); }
      else { taxPairs.push(['GST', 'Not applicable']); }
      taxPairs.push(['Taxable Value', inr(sub)]); taxPairs.push(['Total GST', inr(totGst)]);
      /* Audit fix bug-03: a GST tax invoice must reconcile (Taxable + Total GST [+ round-off] = Total Payable).
         When the owner-entered final amount differs from taxable+GST, show the gap as a Round Off line so the
         printed arithmetic is self-consistent instead of a silent mismatch. */
      var roundOff = Math.round(finalAmt) - Math.round(sub) - totGst;
      if (roundOff !== 0) taxPairs.push(['Round Off', inr(roundOff)]);
      blocks.push({ t: 'kv', cols: 2, pairs: taxPairs });
      blocks.push({ t: 'kv', cols: 1, pairs: [['Total Payable', inr(finalAmt), 'big']] });
      if (del.warrantyTill) blocks.push({ t: 'note', text: 'Service warranty valid until ' + del.warrantyTill + (del.warrantyMonths ? (' (' + del.warrantyMonths + ' months)') : '') + '. Warranty covers only the work carried out.' });
      blocks.push({ t: 'note', text: 'Payment ' + (del.payMode ? ('via ' + del.payMode) : 'received') + (del.payRef ? (' · Ref ' + del.payRef) : '') + '. Computer-generated tax invoice for service-centre / non-franchise sales.' });
      blocks.push({ t: 'sign', boxes: [{ role: 'Customer', name: j.custName || '' }, { role: 'For ' + firmName, name: 'Authorised Signatory' }] });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== ⭐ OWNER MONTHLY BRIEF (portrait) ===== */
    ownerMonthly: function (o) {
      var month = o.month || curMonth();
      var e = expenseMonth(month), pay = G('computePayrollSummary', function () { return {}; })(), qm = monthQms(month);
      var gm = G('computeGroomingMonth', function () { return { avg: 0, totalChecks: 0, daysWithData: 0 }; })(month), ca = G('computeCroAuditMonth', function () { return { avg: 0, totalAudits: 0 }; })(month);
      var aging = G('computeServiceAging', function () { return { totalOpen: 0, b16: 0 }; })(), tax = G('computeTaxStatus', function () { return { overdue: 0, dueWeek: 0 }; })();
      var perf = (function () { try { return computeEmployeePerformance(month) || []; } catch (e) { return []; } })();
      var grossPay = Number(pay.gross) || 0;
      var opSurplus = qm.sales - e.expTot - grossPay; // revenue = actual retail sales (QMS), not the partial expense-ledger income
      var flags = [];
      if (opSurplus < 0) flags.push('Costs exceed sales — operating surplus ' + inr(opSurplus));
      if (qm.walkins >= 50 && qm.conversion < 25) flags.push('Low monthly conversion ' + qm.conversion + '%');
      if (tax.overdue > 0) flags.push(tax.overdue + ' overdue tax filing(s)');
      if (aging.b16 > 0) flags.push(aging.b16 + ' service cases aged 16+ days');
      if (gm.avg && gm.avg < 80) flags.push('Grooming avg ' + gm.avg + '% below standard');
      if (qm.sales && grossPay / qm.sales > 0.4) flags.push('Payroll ' + Math.round(grossPay / qm.sales * 100) + '% of sales — high cost ratio');
      var blocks = [{ t: 'header', title: 'OWNER MONTHLY BRIEF', sub: 'Titan World + Helios · Latur', period: monthLong(month) }];
      blocks.push({ t: 'attn', flags: flags });
      blocks.push({ t: 'kpi', cols: 6, items: [
        { label: 'Retail Sales', value: inr(qm.sales), hero: true },
        { label: 'Conversion', value: pct(qm.conversion) },
        { label: 'Op. Surplus', value: inr(opSurplus), subClass: opSurplus >= 0 ? 'up' : 'down', sub: opSurplus >= 0 ? 'positive' : 'negative' },
        { label: 'Payroll (gross)', value: inr(grossPay) },
        { label: 'Grooming avg', value: (gm.avg || 0) + '%' },
        { label: 'Tax overdue', value: num(tax.overdue) }
      ] });
      blocks.push({ t: 'section', title: 'Profit & Loss', tag: opSurplus >= 0 ? { cls: 'ok', txt: 'Surplus' } : { cls: 'bad', txt: 'Deficit' } });
      blocks.push({ t: 'kv', cols: 2, pairs: [['Retail sales (QMS)', inr(qm.sales)], ['Ledger expenses', inr(e.expTot)], ['Payroll (gross)', inr(grossPay)], ['Operating surplus', inr(opSurplus), 'big'], ['Payroll % of sales', qm.sales ? Math.round(grossPay / qm.sales * 100) + '%' : '—']] });
      blocks.push({ t: 'section', title: 'Sales & Conversion' });
      blocks.push({ t: 'kv', cols: 2, pairs: [['Walk-ins (month)', num(qm.walkins)], ['Purchases', num(qm.purchases)], ['Conversion', pct(qm.conversion)], ['QMS sales ₹', inr(qm.sales)]] });
      blocks.push({ t: 'section', title: 'Staff & Quality' });
      blocks.push({ t: 'kv', cols: 2, pairs: [['Headcount', num(pay.headcount || 0)], ['Payroll gross', inr(pay.gross || 0)], ['Payroll net', inr(pay.net || 0)], ['Payroll run', pay.status || 'draft'], ['Grooming avg', (gm.avg || 0) + '% (' + gm.totalChecks + ' checks)'], ['CRO audit avg', (ca.avg || 0) + '/100 (' + ca.totalAudits + ' audits)']] });
      blocks.push({ t: 'section', title: 'Service & Compliance', tag: ((aging.b16 || 0) > 0 || (tax.overdue || 0) > 0) ? { cls: 'bad', txt: 'Action' } : { cls: 'ok', txt: 'Clear' } });
      blocks.push({ t: 'kv', cols: 2, pairs: [['Open service cases', num(aging.totalOpen)], ['Aged 16+ days', String(aging.b16 || 0)], ['Tax overdue', String(tax.overdue || 0)], ['Tax due this week', num(tax.dueWeek)]] });
      blocks.push({ t: 'section', title: 'Staff Performance — top contributors' });
      var topPerf = perf.slice(0, 8);
      if (topPerf.length) {
        blocks.push({ t: 'table',
          head: [['Employee', 'Role / Firm', 'Grooming', 'Service jobs', 'Cash sheets']],
          body: topPerf.map(function (r) { return [trunc(r.name, 24), trunc(r.firm || r.role || '—', 20), (r.groomChecks ? r.groomPct + '%' : '—'), num(r.serviceJobs || 0), num(r.cashStatements || 0)]; }),
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 'auto' }, 2: { cellWidth: 80, halign: 'right' }, 3: { cellWidth: 90, halign: 'right' }, 4: { cellWidth: 90, halign: 'right' } } });
      } else { blocks.push({ t: 'empty', text: 'No staff performance data.' }); }
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== LOST WALK-IN CALL SHEET (P1-45 · monthly · portrait) =====
       Non-purchase walk-ins for the month, deduped by 10-digit mobile (newest visit kept),
       sorted newest-first. No-number rows are counted + footnoted but excluded from the call
       list. Reads retail_queue_management_v1 directly (mirrors qmsReport's read; writes nothing). */
    lostWalkinCallSheet: function (o) {
      var month = o.month || curMonth();
      var st = J('retail_queue_management_v1', {}) || {};
      var custs = Array.isArray(st.customers) ? st.customers : [], cros = Array.isArray(st.cros) ? st.cros : [];
      var nameOf = function (id) { var c = cros.filter(function (x) { return x.id === id; })[0]; return c ? c.name : 'Unassigned'; };
      var tel10 = function (m) { var d = String(m == null ? '' : m).replace(/\D/g, ''); return d.length >= 10 ? d.slice(-10) : ''; };
      var hdr = { t: 'header', title: 'LOST WALK-IN CALL SHEET', sub: 'Non-purchase follow-ups · Latur', period: monthLong(month) };
      var lost = custs.filter(function (c) { return c && /non.?purchase/i.test(c.outcome || '') && String(c.entryTime || '').slice(0, 7) === month; });
      if (!lost.length) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'No lost walk-ins logged for this month.' }] };
      var noNum = 0, byTel = {};
      lost.forEach(function (c) {
        var t = tel10(c.mobile);
        if (!t) { noNum++; return; }
        var prev = byTel[t];
        if (!prev || String(c.entryTime || '') > String(prev.entryTime || '')) byTel[t] = c;   // keep the newest (warmest) visit
      });
      var rows = Object.keys(byTel).map(function (t) { return byTel[t]; })
        .sort(function (a, b) { return String(b.entryTime || '').localeCompare(String(a.entryTime || '')); });   // newest first
      var callable = rows.length;
      var blocks = [hdr, { t: 'kpi', cols: 3, items: [
        { label: 'Lost Walk-ins', value: num(lost.length), hero: true },
        { label: 'Callable (with #)', value: num(callable) },
        { label: 'No Number', value: num(noNum), sub: noNum ? 'excluded from list' : '', subClass: noNum ? 'down' : 'neu' }
      ] }];
      blocks.push({ t: 'section', title: 'Call-back List — newest first', tag: callable ? { cls: 'warn', txt: num(callable) + ' to call' } : { cls: 'ok', txt: '0' } });
      blocks.push({ t: 'table',
        head: [['#', 'Name', 'Mobile', 'Visit Date', 'CRO', 'Reason / Notes']],
        body: rows.map(function (c, i) {
          var notes = [c.lostReason, c.notes].filter(function (x) { return x != null && String(x).trim() !== ''; }).join(' — ');
          return [String(i + 1), trunc(c.name || '—', 24), tel10(c.mobile), String(c.entryTime || '').slice(0, 10), trunc(nameOf(c.assignedCroId), 18), trunc(notes || '—', 40)];
        }),
        colStyles: { 0: { cellWidth: 30, halign: 'right' }, 1: { cellWidth: 'auto' }, 2: { cellWidth: 84 }, 3: { cellWidth: 80 }, 4: { cellWidth: 'auto' }, 5: { cellWidth: 'auto' } } });
      if (noNum > 0) blocks.push({ t: 'note', text: num(noNum) + ' more lost walk-in' + (noNum === 1 ? '' : 's') + ' had no phone number and could not be added to the call-back list.' });
      return { orientation: 'portrait', blocks: blocks };
    },

    /* ===== WEEKLY BUSINESS SUMMARY (P1-47 · weekly · portrait) =====
       Mon–Sun ISO week containing o.date. Comparison = week-to-date vs the same span of the
       previous week (first nDays up to min(weekEnd, today)). QMS walk-in funnel via
       G('computeQmsDay') per day. Non-buy delta polarity is INVERTED (fewer lost = green). */
    weeklyBrief: function (o) {
      var anchor = o.date || curDate();
      var wk = isoWeek(anchor);
      var today = todayIsoSafe();
      var spanEnd = (wk.end > today) ? today : wk.end;
      var nDays = (spanEnd < wk.start) ? 7 : (wk.days.filter(function (dy) { return dy <= spanEnd; }).length || 1);
      var partial = nDays < 7;
      var prevMon = new Date(wk.start + 'T00:00:00'); prevMon.setDate(prevMon.getDate() - 7);
      var pw = isoWeek(isoOf(prevMon));
      var cur = weekAgg(wk.days.slice(0, nDays));
      var prev = weekAgg(pw.days.slice(0, nDays));
      var hdr = { t: 'header', title: 'WEEKLY BUSINESS SUMMARY', sub: 'Titan World + Helios · Latur', period: weekLabel(wk.start, wk.end) + (partial ? ' · week to date' : '') };
      if (!cur.walkins && !prev.walkins) return { orientation: 'portrait', blocks: [hdr, { t: 'empty', text: 'No walk-in activity recorded for this week.' }] };
      function subOf(c1, p1, invert) {
        var dd = deltaOf(c1, p1);
        var good = invert ? (dd.abs <= 0) : (dd.abs >= 0);
        var txt = (dd.pct == null)
          ? (dd.abs === 0 ? 'same as last wk' : ((dd.abs >= 0 ? '+' : '') + num(dd.abs) + ' vs last wk'))
          : ((dd.pct >= 0 ? '+' : '') + dd.pct + '% vs last wk');
        return { sub: txt, subClass: dd.abs === 0 ? 'neu' : (good ? 'up' : 'down') };
      }
      var convDelta = cur.conversion - prev.conversion;   // percentage-points
      var convSub = { sub: (convDelta >= 0 ? '+' : '') + convDelta + 'pp vs last wk', subClass: convDelta === 0 ? 'neu' : (convDelta > 0 ? 'up' : 'down') };
      var kWalk = subOf(cur.walkins, prev.walkins, false), kPur = subOf(cur.purchases, prev.purchases, false),
          kSal = subOf(cur.sales, prev.sales, false), kNb = subOf(cur.nonPurchase, prev.nonPurchase, true);
      var blocks = [hdr, { t: 'kpi', cols: 5, items: [
        { label: 'Walk-ins', value: num(cur.walkins), sub: kWalk.sub, subClass: kWalk.subClass },
        { label: 'Purchases', value: num(cur.purchases), sub: kPur.sub, subClass: kPur.subClass },
        { label: 'Conversion', value: pct(cur.conversion), sub: convSub.sub, subClass: convSub.subClass, hero: true },
        { label: 'Sales ₹', value: inr(cur.sales), sub: kSal.sub, subClass: kSal.subClass },
        { label: 'Non-buy', value: num(cur.nonPurchase), sub: kNb.sub, subClass: kNb.subClass }
      ] }];
      function chgNum(c1, p1) { var d = c1 - p1; return (d >= 0 ? '+' : '') + num(d); }
      function chgInr(c1, p1) { var d = c1 - p1; return (d >= 0 ? '+' : '-') + inr(Math.abs(d)); }
      function chgPp(c1, p1) { var d = c1 - p1; return (d >= 0 ? '+' : '') + d + 'pp'; }
      blocks.push({ t: 'section', title: 'This Week vs Last Week' });
      blocks.push({ t: 'table',
        head: [['Metric', 'This week', 'Last week', 'Change']],
        body: [
          ['Walk-ins', num(cur.walkins), num(prev.walkins), chgNum(cur.walkins, prev.walkins)],
          ['Purchases', num(cur.purchases), num(prev.purchases), chgNum(cur.purchases, prev.purchases)],
          ['Conversion', pct(cur.conversion), pct(prev.conversion), chgPp(cur.conversion, prev.conversion)],
          ['Sales', inr(cur.sales), inr(prev.sales), chgInr(cur.sales, prev.sales)],
          ['Non-purchase', num(cur.nonPurchase), num(prev.nonPurchase), chgNum(cur.nonPurchase, prev.nonPurchase)]
        ],
        colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 96, halign: 'right' }, 2: { cellWidth: 96, halign: 'right' }, 3: { cellWidth: 96, halign: 'right' } } });
      blocks.push({ t: 'section', title: 'Daily Trend — Mon to Sun' });
      var DOW = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
      blocks.push({ t: 'table',
        head: [['Day', 'Date', 'Walk-ins', 'Purchases', 'Conv %', 'Sales ₹']],
        body: cur.perDay.map(function (p, i) { return [DOW[i] || '', String(p.date).slice(5), num(p.walkins), num(p.purchases), (p.walkins ? p.conv + '%' : '—'), inr(p.sales)]; }),
        raw: cur.perDay.map(function (p) { return [0, 0, 0, 0, 0, p.sales]; }),
        money: [5],
        colStyles: { 0: { cellWidth: 56 }, 1: { cellWidth: 64 }, 2: { cellWidth: 78, halign: 'right' }, 3: { cellWidth: 84, halign: 'right' }, 4: { cellWidth: 68, halign: 'right' }, 5: { cellWidth: 'auto', halign: 'right' } },
        foot: [['Total', '', num(cur.walkins), num(cur.purchases), pct(cur.conversion), inr(cur.sales)]] });
      blocks.push({ t: 'section', title: 'Sales & Conversion — by CRO' });
      var croRows = Object.keys(cur.byCro).map(function (k) { return cur.byCro[k]; }).sort(function (a, b) { return b.walkins - a.walkins; }).slice(0, 8);
      if (croRows.length) {
        blocks.push({ t: 'table',
          head: [['CRO', 'Walk-ins', 'Purchases', 'Conv %', 'Sales ₹']],
          body: croRows.map(function (c) { return [trunc(c.cro, 22), num(c.walkins), num(c.purchases), (c.walkins ? Math.round(c.purchases / c.walkins * 100) + '%' : '—'), inr(c.sales)]; }),
          money: [4],
          colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 96, halign: 'right' }, 2: { cellWidth: 96, halign: 'right' }, 3: { cellWidth: 84, halign: 'right' }, 4: { cellWidth: 130, halign: 'right' } } });
      } else { blocks.push({ t: 'empty', text: 'No CRO activity this week.' }); }
      var pwEnd = (nDays >= 7) ? pw.end : ((pw.days && pw.days[nDays - 1]) || pw.end);   // label the ACTUAL compared sub-span, not the full prior week
      blocks.push({ t: 'note', text: 'Walk-in funnel from QMS. Compared against the same ' + nDays + ' day' + (nDays === 1 ? '' : 's') + ' of the previous week (' + weekLabel(pw.start, pwEnd) + '). Days with no entries count as zero.' });
      return { orientation: 'portrait', blocks: blocks };
    }
  };

  var META = {
    ownerBrief: { title: 'Daily Owner Brief', scope: 'daily', icon: '📊' },
    cashStatement: { title: 'Daily Cash Statement', scope: 'daily', icon: '💵' },
    stockRegister: { title: 'Stock Closing Register', scope: 'daily', icon: '📦' },
    dsrRegister: { title: 'Daily Sales Register (DSR)', scope: 'daily', icon: '🧾' },
    qmsReport: { title: 'Queue & Conversion (QMS)', scope: 'daily', icon: '🎯' },
    croAudit: { title: 'CRO Audit Scorecard', scope: 'daily', icon: '✅' },
    qmsEodSummary: { title: 'QMS — End of Day Summary', scope: 'daily', icon: '🌙' },
    payrollRegister: { title: 'Payroll — Salary Register', scope: 'monthly', icon: '💼' },
    payrollSlip: { title: 'Payroll — Salary Slip', scope: 'monthly', icon: '🧾' },
    statutorySummary: { title: 'Payroll — Statutory Summary', scope: 'monthly', icon: '🏛️' },
    statutoryRegister: { title: 'Payroll — Statutory Register (Member-wise)', scope: 'monthly', icon: '🏛️' },
    advanceVoucher: { title: 'Payroll — Advance Voucher', scope: 'monthly', icon: '🧾' },
    hrLetter: { title: 'Payroll — HR Letter', scope: 'monthly', icon: '📜' },
    fnfSettlement: { title: 'Payroll — Full & Final Settlement', scope: 'monthly', icon: '🤝' },
    leaveRegister: { title: 'Leave Register', scope: 'monthly', icon: '🌴' },
    groomingDaily: { title: 'Grooming — Daily Audit', scope: 'daily', icon: '✨' },
    groomingMonthly: { title: 'Grooming — Monthly Trend', scope: 'monthly', icon: '📈' },
    expenseMonthly: { title: 'Monthly Expense & P&L', scope: 'monthly', icon: '📒' },
    taxReport: { title: 'Tax Compliance — Due Report', scope: 'monthly', icon: '⚖️' },
    serviceAging: { title: 'Service — Open Cases Aging', scope: 'daily', icon: '🛠️' },
    serviceJobCard: { title: 'Service — Job Card', scope: 'daily', icon: '📋' },
    serviceTaxInvoice: { title: 'Service — GST Tax Invoice', scope: 'daily', icon: '🧾' },
    ownerMonthly: { title: 'Owner Monthly Brief', scope: 'monthly', icon: '📊' },
    weeklyBrief: { title: 'Weekly Business Summary', scope: 'weekly', icon: '📅' },
    lostWalkinCallSheet: { title: 'Lost Walk-in Call Sheet', scope: 'monthly', icon: '📞' }
  };

  /* ============================================================================
     R6 — NATIVE VECTOR TEXT ENGINE  (jsPDF + jspdf-autotable)
     Builders that return { orientation, blocks:[...] } render here (crisp, small,
     selectable, row-safe pagination). Legacy renderPages() below still serves
     builders that still return { pages:[html] } — dual-path in SaagarReport.build.
     ============================================================================ */
  function renderCompress(){ try { return !(window.__R6_TEST_NOCOMPRESS); } catch (e) { return true; } }

  var PAL = {
    NAVY:[13,35,64], NAVY2:[19,50,92], GOLD:[184,146,58], INK:[26,36,51],
    MUT:[100,116,139], LINE:[227,232,240], BG:[247,249,252],
    WHITE:[255,255,255], FOOT:[110,124,148],
    GREEN:[27,143,90], AMBER:[183,121,31], RED:[192,57,43],
    OK_FILL:[224,242,231], WARN_FILL:[253,239,214], BAD_FILL:[251,228,224],
    ZEBRA:[237,241,247], TOTAL_FILL:[235,240,247], FLAG_FILL:[252,231,227],
    SECTION_FILL:[238,243,250], KPI_HERO_LAB:[200,212,232], KPI_HERO_SUB:[150,224,188]
  };
  var FZ = {
    brandMain:18, brandSub:7, addr:9, rTitle:12, rSub:9, rgen:8,
    sectionTitle:9.5, headerRow:8.5, tableBody:9.5,
    kpiLabel:7.5, kpiValue:15, kpiValueMin:10, kpiSub:8,
    attnHead:9.5, attnItem:9.5, kv:9.5, kvBig:11,
    statline:9, netLabel:11, netValue:28, sign:9, footer:8.5, note:8, empty:11, carry:9
  };
  function freshAT(){
    return {
      styles:{ font:'DMSans', fontSize:FZ.tableBody, cellPadding:{ top:3.5, right:3.5, bottom:3.5, left:4 }, textColor:PAL.INK, lineColor:PAL.LINE, lineWidth:0.5, overflow:'linebreak', valign:'top' },
      headStyles:{ font:'DMSans', fontStyle:'bold', fillColor:PAL.NAVY, textColor:PAL.WHITE, fontSize:FZ.headerRow, halign:'left', lineColor:PAL.NAVY, lineWidth:0, valign:'middle' },
      bodyStyles:{ fillColor:PAL.WHITE },
      alternateRowStyles:{ fillColor:PAL.ZEBRA },
      footStyles:{ font:'DMSans', fontStyle:'bold', fillColor:PAL.TOTAL_FILL, textColor:PAL.NAVY, lineColor:PAL.NAVY, lineWidth:0, halign:'right' },
      theme:'grid'
    };
  }
  function _fill(d,c){ d.setFillColor(c[0],c[1],c[2]); }
  function _stroke(d,c){ d.setDrawColor(c[0],c[1],c[2]); }
  function _txt(d,c){ d.setTextColor(c[0],c[1],c[2]); }
  function _setf(d,style,size){ d.setFont('DMSans', style||'normal'); d.setFontSize(size); }
  function _up(s){ return String(s==null?'':s).toUpperCase(); }
  function subColor(cls){ return cls==='up'?PAL.GREEN : (cls==='down'?PAL.RED : PAL.MUT); }
  function genLine(){ return (ownerNm()? 'Prepared for '+ownerNm()+' · ':'') + 'Generated '+stamp(); }

  var SAN = {
    text: function(s){ return String(s==null?'':s)
        .replace(/[\u{1F000}-\u{1FAFF}]/gu,'').replace(/[\u{2600}-\u{27BF}]/gu,'')
        .replace(/[✅⛔✔✖✗️⭐]/g,'')
        .replace(/≥/g,'>=').replace(/≤/g,'<=').replace(/…/g,'...')
        .replace(/\s+/g,' ').trim(); },
    flags: function(arr){ return (arr||[]).map(SAN.text).filter(Boolean); }
  };
  function setFonts(doc){
    doc.setFont('DMSans','normal');
    var fl = doc.getFontList();
    if(!(fl.DMSans && fl.DMSans.indexOf('normal')>=0 && fl.DMSans.indexOf('bold')>=0))
      throw new Error('R6 fatal: DMSans normal+bold not registered — rupee/bold would corrupt');
  }

  /* ---- running header / footer (idempotent per page) ---- */
  function drawChip(doc, x, yTop, label, kind){
    label = SAN.text(label); _setf(doc,'bold',8);
    var w = doc.getTextWidth(label) + (kind==='locked'?20:9) + 7, h=15;
    var f = kind==='draft'?PAL.AMBER : (kind==='ok'?PAL.GREEN : PAL.NAVY);
    _fill(doc,f); doc.roundedRect(x, yTop, w, h, 4,4,'F');
    var tx = x + (kind==='locked'?20:8);
    if(kind==='locked'){
      _fill(doc,PAL.WHITE); doc.rect(x+7, yTop+7, 7,5,'F');
      _stroke(doc,PAL.WHITE); doc.setLineWidth(1);
      doc.line(x+8.5,yTop+7,x+8.5,yTop+5); doc.line(x+8.5,yTop+5,x+12,yTop+5); doc.line(x+12,yTop+5,x+12,yTop+7);
    }
    _txt(doc,PAL.WHITE); _setf(doc,'bold',8); doc.text(label, tx, yTop+10.4);
    return x+w;
  }
  function drawRunningHeader(doc, pageW, M, hdr, cont){
    var L=M.left, R=pageW-M.right;
    _setf(doc,'bold',FZ.brandMain); _txt(doc,PAL.NAVY); doc.text('Saagar Traders', L, 44);
    _setf(doc,'bold',FZ.brandSub); _txt(doc,PAL.GOLD); doc.text('BUSINESS CONTROL CENTRE', L, 54, {charSpace:0.8});
    if(hdr.addr){ _setf(doc,'normal',FZ.addr); _txt(doc,PAL.MUT); doc.text(SAN.text(hdr.addr), L, 66); }
    else if(hdr.chip){ drawChip(doc, L, 60, hdr.chip, hdr.chipKind); }
    _txt(doc,PAL.NAVY); _setf(doc,'bold',FZ.rTitle);
    doc.text(SAN.text(hdr.title||'') + (cont?'  (continued)':''), R, 40, {align:'right'});
    if(hdr.sub){ _setf(doc,'normal',FZ.rSub); _txt(doc,PAL.MUT); doc.text(SAN.text(hdr.sub), R, 52, {align:'right'}); }
    if(hdr.period){ _setf(doc,'bold',FZ.rSub); _txt(doc,PAL.INK); doc.text(SAN.text(hdr.period), R, 63, {align:'right'}); }
    _setf(doc,'normal',FZ.rgen); _txt(doc,PAL.FOOT); doc.text(genLine(), R, 74, {align:'right'});
    _stroke(doc,PAL.GOLD); doc.setLineWidth(1.2); doc.line(L, 84, R, 84);
  }
  function drawFooter(doc, pageW, M, pg, tot){
    var pageH=doc.internal.pageSize.getHeight(), L=M.left, R=pageW-M.right, y=pageH-34;
    _stroke(doc,PAL.LINE); doc.setLineWidth(0.5); doc.line(L, y-10, R, y-10);
    _setf(doc,'normal',FZ.footer); _txt(doc,PAL.FOOT);
    doc.text('Saagar Traders · Latur · Confidential — for owner review', L, y);
    doc.text('Business Control Centre · Page ' + pg + ' of ' + tot, R, y, { align:'right' });   // literal total (known at final furniture pass) — no putTotalPages
  }

  /* ---- raw-draw content primitives ---- */
  function kpiTiles(doc, x, y, w, items, cols){
    cols = cols || items.length;
    var perRow = cols; if (w/cols < 92) perRow = Math.ceil(cols/2);
    var gap=8, rows=Math.ceil(items.length/perRow), tileW=(w-gap*(perRow-1))/perRow, tileH=46;
    for(var i=0;i<items.length;i++){
      var r=Math.floor(i/perRow), c=i%perRow, it=items[i];
      var tx=x+c*(tileW+gap), ty=y+r*(tileH+gap);
      _fill(doc, it.hero?PAL.NAVY:PAL.BG); doc.roundedRect(tx,ty,tileW,tileH,5,5,'F');
      if(!it.hero){ _stroke(doc,PAL.LINE); doc.setLineWidth(0.5); doc.roundedRect(tx,ty,tileW,tileH,5,5,'S'); }
      _setf(doc,'bold',FZ.kpiLabel); _txt(doc, it.hero?PAL.KPI_HERO_LAB:PAL.MUT);
      doc.text(_up(SAN.text(it.label)), tx+8, ty+14);
      var fs=FZ.kpiValue, val=SAN.text(String(it.value)); _setf(doc,'bold',fs);
      while(doc.getTextWidth(val) > tileW-14 && fs>FZ.kpiValueMin){ fs-=0.5; doc.setFontSize(fs); }
      _txt(doc, it.hero?PAL.WHITE:PAL.NAVY); doc.text(val, tx+8, ty+32);
      if(it.sub){ _setf(doc,'normal',FZ.kpiSub); _txt(doc, it.hero?PAL.KPI_HERO_SUB:subColor(it.subClass)); doc.text(SAN.text(String(it.sub)), tx+8, ty+42); }
    }
    return y + rows*tileH + (rows-1)*gap + 12;
  }
  function sectionTitle(doc, x, y, w, title, tag){
    y += 8;
    _fill(doc,PAL.GOLD); doc.rect(x, y-8, 3, 12, 'F');
    _setf(doc,'bold',FZ.sectionTitle); _txt(doc,PAL.NAVY); doc.text(_up(SAN.text(title)), x+9, y+1);
    if(tag && tag.txt){ _setf(doc,'bold',7.5);
      var tw=doc.getTextWidth(SAN.text(tag.txt))+12;
      var f = tag.cls==='warn'?PAL.WARN_FILL : (tag.cls==='bad'?PAL.BAD_FILL : PAL.OK_FILL);
      var tc = tag.cls==='warn'?PAL.AMBER : (tag.cls==='bad'?PAL.RED : PAL.GREEN);
      _fill(doc,f); doc.roundedRect(x+w-tw, y-8, tw, 12, 3,3,'F');
      _txt(doc,tc); doc.text(SAN.text(tag.txt), x+w-tw+6, y+0.5);
    }
    return y+8;
  }
  function kvBlock(doc, x, y, w, pairs, cols){
    cols = cols||2; var gap=12, colW=(w-gap*(cols-1))/cols, rowH=17, rows=Math.ceil(pairs.length/cols);
    for(var i=0;i<pairs.length;i++){
      var r=Math.floor(i/cols), c=i%cols, px=x+c*(colW+gap), py=y+r*rowH+11;
      var k=SAN.text(pairs[i][0]), v=SAN.text(String(pairs[i][1])), big=pairs[i][2]==='big';
      _setf(doc,'normal',FZ.kv); _txt(doc,PAL.MUT); doc.text(k, px, py);
      _setf(doc,'bold',big?FZ.kvBig:FZ.kv); _txt(doc,PAL.INK); doc.text(v, px+colW, py, {align:'right'});
      _stroke(doc,PAL.LINE); doc.setLineWidth(0.4); doc.line(px, py+4, px+colW, py+4);
    }
    return y + rows*rowH + 8;
  }
  function statLine(doc, x, y, w, spans){
    y+=4; var px=x;
    spans.forEach(function(s){
      _setf(doc,'normal',FZ.statline); _txt(doc,PAL.MUT);
      var lab=SAN.text(s[0])+': '; doc.text(lab, px, y+9); var lw=doc.getTextWidth(lab);
      _setf(doc,'bold',FZ.statline); _txt(doc,PAL.NAVY); var val=SAN.text(String(s[1])); doc.text(val, px+lw, y+9);
      px += lw + doc.getTextWidth(val) + 18;
    });
    return y+18;
  }
  function netBox(doc, x, y, w, label, value){
    var h=54; _fill(doc,PAL.NAVY); doc.roundedRect(x,y,w,h,6,6,'F');
    _setf(doc,'bold',FZ.netLabel); _txt(doc,PAL.KPI_HERO_LAB); doc.text(_up(SAN.text(label)), x+16, y+22);
    _setf(doc,'bold',FZ.netValue); _txt(doc,PAL.WHITE); doc.text(SAN.text(String(value)), x+w-16, y+38, {align:'right'});
    return y+h+10;
  }
  function signRow(doc, x, y, w, boxes){
    y+=26; var n=boxes.length, gap=22, bw=(w-gap*(n-1))/n;
    for(var i=0;i<n;i++){ var bx=x+i*(bw+gap);
      if(boxes[i].name){ _txt(doc,PAL.INK); _setf(doc,'bold',FZ.sign); doc.text(SAN.text(boxes[i].name), bx, y-4); }
      _stroke(doc,PAL.MUT); doc.setLineWidth(0.6); doc.line(bx, y, bx+bw-12, y);
      _setf(doc,'normal',FZ.sign); _txt(doc,PAL.MUT); doc.text(SAN.text(boxes[i].role||''), bx, y+12);
    }
    return y+20;
  }
  function noteLine(doc, x, y, w, text, color){
    _setf(doc,'normal',FZ.note); _txt(doc, color||PAL.MUT);
    var lines=doc.splitTextToSize(SAN.text(text), w); doc.text(lines, x, y+9); return y + lines.length*11 + 6;
  }
  // Body prose paragraph (wrapped) — for free-form documents like HR letters.
  function paraLine(doc, x, y, w, text){
    _setf(doc,'normal',FZ.tableBody); _txt(doc, PAL.INK);
    var lines=doc.splitTextToSize(SAN.text(text), w); doc.text(lines, x, y+11); return y + lines.length*13 + 6;
  }
  function emptyMsg(doc, x, y, w, text){
    y+=40; _setf(doc,'normal',FZ.empty); _txt(doc,PAL.MUT); doc.text(SAN.text(text), x+w/2, y, {align:'center'}); return y+30;
  }
  function attnBanner(doc, M, y, w, flags, pageFurniture){
    var x=M.left, pageH=doc.internal.pageSize.getHeight();
    var clear=!flags||!flags.length, items=clear?['All clear - nothing needs you today']:flags;
    var mr=7, hy=y+13;
    if(clear){ _fill(doc,PAL.GREEN); doc.circle(x+mr, hy, mr, 'F'); _stroke(doc,PAL.WHITE); doc.setLineWidth(1.4); doc.line(x+mr-3,hy,x+mr-1,hy+3); doc.line(x+mr-1,hy+3,x+mr+3.5,hy-2.5); }
    else { _fill(doc,PAL.RED); doc.circle(x+mr, hy, mr, 'F'); _txt(doc,PAL.WHITE); _setf(doc,'bold',11); doc.text('!', x+mr, hy+3.6, {align:'center'}); }
    _setf(doc,'bold',FZ.attnHead); _txt(doc, clear?PAL.GREEN:PAL.RED);
    doc.text(clear?'ALL CLEAR':'NEEDS YOUR ATTENTION TODAY', x+mr*2+8, hy+3);
    y = hy + 14; var lh=FZ.attnItem*1.4;
    items.forEach(function(f){
      _setf(doc,'normal',FZ.attnItem); _txt(doc,PAL.INK);
      var lines=doc.splitTextToSize('• '+SAN.text(f), w-10);
      lines.forEach(function(ln){
        if(y+lh > pageH-M.bottom){ doc.addPage(); pageFurniture(); y=M.top; }
        _setf(doc,'normal',FZ.attnItem); _txt(doc,PAL.INK); doc.text(ln, x+4, y+9); y+=lh;
      });
    });
    return y+8;
  }

  /* ---- table support: formatting, pills, deterministic carry ---- */
  function formatCell(d, blk){
    if(d.cell && d.cell.text && d.cell.text.length){ d.cell.text = d.cell.text.map(function(t){ return SAN.text(t); }); }  // strip any stray emoji/symbol in real data
    if(d.section==='foot'){ d.cell.styles.halign=(d.column.index===0?'left':'right'); return; }
    if(d.section!=='body') return;
    if(blk.money && blk.money.indexOf(d.column.index)>=0){ d.cell.styles.halign='right'; d.cell.styles.fontStyle='bold'; d.cell.styles.textColor=PAL.NAVY; }
    if(blk.flagRows && blk.flagRows[d.row.index]) d.cell.styles.fillColor=PAL.FLAG_FILL;
    if(blk.pills && blk.pills.indexOf(d.column.index)>=0) d.cell.text=[];
  }
  function drawPill(d, blk){
    if(d.section!=='body' || !blk.pills || blk.pills.indexOf(d.column.index)<0) return;
    var src=(blk.body[d.row.index]||[])[d.column.index];
    var label=SAN.text(String(src==null?'':src)); if(!label) return;
    var doc=d.doc, fillc, tc;
    if(/\b(ok|done|filed|balanced|delivered|closed|paid|pass|passed)\b/i.test(label)){ fillc=PAL.OK_FILL; tc=PAL.GREEN; }
    else if(/\b(overdue|mismatch|bad|theft|aged|short|fail|failed|missing)\b/i.test(label)){ fillc=PAL.BAD_FILL; tc=PAL.RED; }
    else { fillc=PAL.WARN_FILL; tc=PAL.AMBER; }
    _setf(doc,'bold',FZ.tableBody-0.5);
    var tw=doc.getTextWidth(label), pillW=Math.min(tw+12, d.cell.width-2), h=Math.min(d.cell.height-4, FZ.tableBody+5);
    var cx=d.cell.x+(d.cell.width-pillW)/2, cy=d.cell.y+(d.cell.height-h)/2;
    _fill(doc,fillc); doc.roundedRect(cx,cy,pillW,h,5,5,'F');
    _txt(doc,tc); doc.text(label, d.cell.x+d.cell.width/2, cy+h/2+2.6, {align:'center'});
  }
  function fmtLD(v){ v=Number(v)||0; return (v%1)? v.toFixed(1) : String(Math.round(v)); }
  function trunc(s, n){ s=String(s==null?'':s); return s.length>n ? s.slice(0, n-2)+'...' : s; }
  function fmtCarry(blk, ci, val){ var f=(blk.fmt&&blk.fmt[ci])||inr; return f(val); }
  function prepCarry(blk){
    blk._cum={};
    (blk.money||[]).forEach(function(ci){ var run=0, arr=[]; for(var r=0;r<blk.raw.length;r++){ run+=(Number(blk.raw[r][ci])||0); arr.push(run); } blk._cum[ci]=arr; });
  }
  function drawCarryLine(doc, M, y, label, blk, vals, cols){
    var pageW=doc.internal.pageSize.getWidth();
    _setf(doc,'bold',FZ.carry); _txt(doc,PAL.NAVY); doc.text(label, M.left+2, y);
    blk.money.forEach(function(ci,k){
      if(cols && cols[ci]!=null && cols[ci].x!=null){ doc.text(vals[k], cols[ci].x+cols[ci].width-3.5, y, {align:'right'}); }
      else { doc.text(vals[k], pageW-M.right, y, {align:'right'}); }
    });
  }
  function drawCarryAfter(doc, blk, carry, M){
    var pages=Object.keys(carry.pageRows).map(Number).sort(function(a,b){return a-b;});
    if(pages.length<2) return;
    var cols=(doc.lastAutoTable && doc.lastAutoTable.columns) || null;
    pages.forEach(function(absP, idx){
      var rec=carry.pageRows[absP]; doc.setPage(absP);
      if(idx>0){ var bf=blk.money.map(function(ci){ return fmtCarry(blk,ci, rec.min>0?blk._cum[ci][rec.min-1]:0); });
        drawCarryLine(doc, M, (M.top+16)-5, 'Brought forward', blk, bf, cols); }
      if(idx<pages.length-1){ var cf=blk.money.map(function(ci){ return fmtCarry(blk,ci, blk._cum[ci][rec.max]); });
        drawCarryLine(doc, M, rec.yBottom+13, 'Carried forward', blk, cf, cols); }
    });
  }
  function tableOpts(blk, startY, M, pageFurniture, carry){
    var o = freshAT();
    o.head=blk.head; o.body=blk.body; if(blk.foot) o.foot=blk.foot;
    o.startY=startY;
    o.margin={ top:(carry?M.top+16:M.top), bottom:(carry?78:M.bottom), left:M.left, right:M.right };
    o.showHead='everyPage'; o.showFoot=blk.foot?'lastPage':'never';
    o.rowPageBreak='avoid'; o.pageBreak='auto';
    if(blk.colStyles) o.columnStyles=blk.colStyles;
    o.didParseCell=function(d){ formatCell(d, blk); };
    o.didDrawCell=function(d){
      drawPill(d, blk);
      if(carry && d.section==='body'){
        var abs=d.doc.internal.getCurrentPageInfo().pageNumber;
        var rec=carry.pageRows[abs]||(carry.pageRows[abs]={min:1e9,max:-1,yBottom:0});
        if(d.row.index<rec.min) rec.min=d.row.index;
        if(d.row.index>rec.max) rec.max=d.row.index;
        var yb=d.cell.y+d.cell.height; if(yb>rec.yBottom) rec.yBottom=yb;
      }
    };
    o.didDrawPage=function(d){ pageFurniture(); };
    return o;
  }

  /* ---- the renderer ---- */
  /* Body blocks for ONE salary slip (no header) — shared by the single-slip builder and the
     batch builders, so a slip looks identical whether exported alone, zipped, or combined. */
  function slipBlocks(s, leadTitle){
    var b = [{ t: 'section', title: leadTitle || 'Employee & Attendance' }];
    b.push({ t: 'kv', cols: 2, pairs: [
      ['Employee', s.name || '—'], ['Employee ID', s.empId || '—'],
      ['Gender', s.gender || '—'], ['Pay Period', s.period || '—'],
      ['Total Days', s.totalDays != null ? String(s.totalDays) : '—'], ['Salary Days', s.salaryDays != null ? String(s.salaryDays) : '—'],
      ['Deduction Days', s.deductionDays != null ? String(s.deductionDays) : '—'], ['Overtime Days', s.otDays != null ? String(s.otDays) : '—'],
      ['Bank', s.bankName || '—'], ['A/C No.', s.accountNo || '—'],
      ['IFSC', s.ifsc || '—'], ['Payment Mode', s.paidMode || '—']
    ] });
    b.push({ t: 'table',
      head: [['Earnings', 'Amount (₹)', 'Deductions', 'Amount (₹)']],
      body: [
        ['Basic', inr(s.basic), 'Professional Tax', inr(s.pt)],
        ['HRA', inr(s.hra), 'PF (Employee)', inr(s.pf)],
        ['Washing Allowance', inr(s.washing), 'ESIC (Employee)', inr(s.esic)],
        ['Overtime', inr(s.ot), 'Advance Deduction', inr(s.advance)]
      ],
      raw: [[0, s.basic, 0, s.pt], [0, s.hra, 0, s.pf], [0, s.washing, 0, s.esic], [0, s.ot, 0, s.advance]], money: [1, 3],
      colStyles: { 0: { cellWidth: 'auto' }, 1: { cellWidth: 96, halign: 'right' }, 2: { cellWidth: 'auto' }, 3: { cellWidth: 96, halign: 'right' } },
      foot: [['Gross Payable + OT' + (s.showRes ? ' *' : ''), inr(s.earnings), 'Total Deductions', inr(s.deductions)]] });
    b.push({ t: 'netbox', label: 'Net Pay (Take Home)', value: inr(s.net) });
    b.push({ t: 'note', text: 'In words: Rupees ' + (s.netWords || '') + ' Only.' });
    if (s.showRes) b.push({ t: 'note', text: '* Gross Salary Payable (' + inr(s.grossPayable) + ') is the pro-rated gross per the agreed pay structure and includes all applicable allowances over and above the components listed (Special Allowance: ' + inr(s.residual) + ').' });
    if (s.salaryRemark) b.push({ t: 'note', text: 'Remark: ' + s.salaryRemark });
    if (s.paidRef) b.push({ t: 'note', text: 'Payment: ' + (s.paidMode || '') + ' · Ref/UTR ' + s.paidRef + (s.paidDate ? ' · Paid on ' + s.paidDate : '') });
    b.push({ t: 'sign', boxes: [{ role: 'Employee Signature', name: s.name || '' }, { role: 'For Saagar Traders', name: s.approvedBy || 'Authorised Signatory' }] });
    return b;
  }

  function renderDoc(blocks, orientation){
    var portrait = orientation!=='landscape';
    var JsPDF=pdfLib(); if(!JsPDF) throw new Error('R6: jsPDF not loaded');
    var doc=new JsPDF({ unit:'pt', format:'a4', orientation:portrait?'portrait':'landscape', compress:renderCompress() });
    if(typeof doc.autoTable!=='function') throw new Error('R6: jspdf-autotable not loaded');
    setFonts(doc);
    var pageW=doc.internal.pageSize.getWidth();
    var M = portrait ? { top:96,bottom:56,left:44,right:44 } : { top:96,bottom:56,left:40,right:40 };
    var contentW=pageW-M.left-M.right;
    var hdr=null; for(var i=0;i<blocks.length;i++){ if(blocks[i].t==='header'){ hdr=blocks[i]; break; } } hdr=hdr||{};
    var DRAWN={};
    function pageFurniture(){                          // header only; footer drawn in final pass (needs total)
      var p=doc.internal.getCurrentPageInfo().pageNumber;
      if(DRAWN[p]) return; DRAWN[p]=true;
      drawRunningHeader(doc,pageW,M,hdr, p>1 && hdr.cont);
    }
    var y=M.top;
    blocks.forEach(function(blk, _i){
      switch(blk.t){
        case 'header': break;
        case 'spacer': y+=(blk.h||10); break;
        case 'pagebreak': doc.addPage(); pageFurniture(); y=M.top; break;
        case 'para': y=paraLine(doc,M.left,y,contentW,blk.text); break;
        case 'attn': y=attnBanner(doc,M,y,contentW,blk.flags,pageFurniture); break;
        case 'kpi': y=kpiTiles(doc,M.left,y,contentW,blk.items,blk.cols); break;
        case 'section': y=sectionTitle(doc,M.left,y,contentW,blk.title,blk.tag); break;
        case 'kv': y=kvBlock(doc,M.left,y,contentW,blk.pairs,blk.cols); break;
        case 'statline': y=statLine(doc,M.left,y,contentW,blk.spans); break;
        case 'netbox': y=netBox(doc,M.left,y,contentW,blk.label,blk.value); break;
        case 'sign': y=signRow(doc,M.left,y,contentW,blk.boxes); break;
        case 'note': y=noteLine(doc,M.left,y,contentW,blk.text,blk.color); break;
        case 'empty': y=emptyMsg(doc,M.left,y,contentW,blk.text); break;
        case 'table':
          var carry=null; if(blk.money && blk.raw){ prepCarry(blk); carry={pageRows:{}}; }
          doc.autoTable(tableOpts(blk, y, M, pageFurniture, carry));
          if(carry) drawCarryAfter(doc, blk, carry, M);
          y=doc.lastAutoTable.finalY + 8;
          break;
      }
      if (_i < blocks.length - 1) y = maybeBreak(doc, y, M, pageFurniture);   // never add a trailing empty page after the last block
    });
    var tot=doc.internal.getNumberOfPages();
    for(var p=1;p<=tot;p++){
      doc.setPage(p);
      if(!DRAWN[p]){ DRAWN[p]=true; drawRunningHeader(doc,pageW,M,hdr, p>1 && hdr.cont); }
      drawFooter(doc,pageW,M,p,tot);
    }
    return doc;
  }
  function maybeBreak(doc, y, M, pageFurniture){
    var pageH=doc.internal.pageSize.getHeight();
    if(y > pageH - M.bottom - 8){ doc.addPage(); pageFurniture(); return M.top; }
    return y;
  }

  /* ---------- render: page HTML → html2canvas → jsPDF (one image per A4 page) ---------- */
  function pdfLib() {
    if (window.jspdf && window.jspdf.jsPDF) return window.jspdf.jsPDF;
    if (window.jsPDF) return window.jsPDF;
    return null;
  }
  /* renderPages() (html2canvas raster slicer) removed in R6.7 — every report is now native vector via renderDoc(). */

  /* ---------- P1-48: locked-run statutory totals (Month-end pack fallback) ----------
     Reads the statTotals the payroll module freezes at lockRun() so statutorySummary can
     build from storage when live opts are absent (pack path). Hardcoded English month
     names match payroll's MONTHS/pkey() key format ('2026-07' → 'July-2026') — never
     toLocaleString. Returns null unless the run is locked AND statTotals is an object;
     callers then keep the existing "open the module" empty-note. */
  function _payrollLockedTotals(ym) {           // ym = 'YYYY-MM' from pack()
    try {
      var MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
      var p = String(ym || '').split('-'); if (p.length < 2) return null;
      var key = MONTHS[Number(p[1]) - 1] + '-' + Number(p[0]);      // 'July-2026' — payroll pkey() format
      var st = JSON.parse(localStorage.getItem('payroll_suite_v1_2026') || '{}');
      var run = st.runs && st.runs[key];
      if (run && run.status === 'locked' && run.statTotals && typeof run.statTotals === 'object'
          && Number(run.statTotals.emp) > 0)   // a month locked with zero employees carries an all-zero statTotals; treat as no-data so the pack shows the empty-note, not a page of ₹0s
        return { totals: run.statTotals, preparedBy: run.preparedBy || '', approvedBy: run.approvedBy || '', period: key.replace('-', ' ') };
    } catch (e) {}
    return null;
  }

  /* ---------- public API ---------- */
  function buildModel(type, opts) {
    var b = BUILDERS[type]; if (!b) throw new Error('Unknown report: ' + type);
    return b(opts || {});
  }
  function filename(type, opts) {
    var d = (opts && opts.date) || curDate();
    return 'Saagar_' + type + '_' + d + '.pdf';
  }
  function modelRowCount(model) {
    return ((model && model.blocks) || []).reduce(function (total, block) {
      return total + (block && Array.isArray(block.body) ? block.body.length : 0);
    }, 0);
  }
  function reportRowCount(type, opts) {
    try { return modelRowCount(buildModel(type, opts || {})); } catch (e) { return 0; }
  }
  function authorizeExport(meta) {
    var api = window.SaagarExportControl;
    if (!api || typeof api.authorize !== 'function') {
      try { toast('Export control unavailable — report delivery is blocked.'); } catch (e) {}
      return false;
    }
    return api.authorize(meta);
  }
  function finishExport(token, outcome) {
    try { return !!(window.SaagarExportControl && window.SaagarExportControl.recordOutcome(token, outcome)); }
    catch (e) { return false; }
  }
  function beginExport(token) {
    try { return !!(window.SaagarExportControl && window.SaagarExportControl.beginDelivery(token)); }
    catch (e) { return false; }
  }
  function shareBlob(blob, fname, token) {
    if (!beginExport(token)) {
      try { toast('Report delivery blocked — approval token is no longer valid.'); } catch (e) {}
      return Promise.resolve({ blocked: true });
    }
    var c = (function () { try { return capsShare(); } catch (e) { return null; } })();
    if (c) {
      return new Promise(function (res) { var fr = new FileReader(); fr.onloadend = function () { res(String(fr.result).split(',')[1]); }; fr.readAsDataURL(blob); })
        .then(function (b64) { return c.FS.writeFile({ path: fname, data: b64, directory: 'CACHE' }); })
        .then(function () { return c.FS.getUri({ directory: 'CACHE', path: fname }); })
        .then(function (r) { return c.Share.share({ title: fname, text: 'Saagar Traders — ' + fname, files: [r.uri], dialogTitle: 'Share report via WhatsApp' }); })
        .then(function () { finishExport(token, 'shared'); try { toast('Pick WhatsApp in the share menu'); } catch (e) {} })
        .catch(function (e) {
          var m = (e && e.message) || String(e), cancelled = /cancel|dismiss/i.test(m);
          finishExport(token, cancelled ? 'cancelled' : 'failed');
          if (!cancelled) { try { toast('Share failed: ' + m); } catch (_) {} }
        });
    }
    var a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = fname; a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); }, 1500);
    finishExport(token, 'downloaded');
    try { toast('Report downloaded'); } catch (e) {}
    return Promise.resolve();
  }
  var CSV_REPORTS = {
    payrollRegister: true,
    expenseMonthly: true,
    leaveRegister: true,
    stockRegister: true
  };
  function csvCell(value) {
    var text = String(value == null ? '' : value);
    if (/^[\t\r\n ]*[=+\-@]/.test(text)) text = "'" + text;
    return '"' + text.replace(/"/g, '""') + '"';
  }
  function blocksToCsv(model) {
    var rows = [];
    (model.blocks || []).forEach(function (block) {
      if (!block || !Array.isArray(block.head) || !Array.isArray(block.body)) return;
      if (rows.length) rows.push([]);
      rows.push(block.head);
      block.body.forEach(function (row) { rows.push(Array.isArray(row) ? row : [row]); });
      if (Array.isArray(block.foot)) {
        if (block.foot.length && Array.isArray(block.foot[0])) block.foot.forEach(function (row) { rows.push(row); });
        else rows.push(block.foot);
      }
    });
    if (!rows.length) throw new Error('This report has no tabular rows for CSV');
    var text = '\ufeff' + rows.map(function (row) { return row.map(csvCell).join(','); }).join('\r\n');
    return {
      text: text,
      blob: new Blob([text], { type: 'text/csv;charset=utf-8' }),
      rowCount: modelRowCount(model)
    };
  }
  function modelToCsv(type, opts) { return blocksToCsv(buildModel(type, opts || {})); }
  // JSZip is only needed for the per-employee ZIP export — load it on demand (it is NOT in the
  // report-engine core gate, so a missing/failed JSZip never blocks normal reports).
  function ensureZip() {
    if (window.JSZip) return Promise.resolve();
    return new Promise(function (res, rej) {
      var s = document.createElement('script'); s.src = 'jszip.min.js';
      s.onload = function () { res(); }; s.onerror = function () { rej(new Error('JSZip failed to load')); };
      (document.body || document.documentElement).appendChild(s);
    });
  }

  window.SaagarReport = {
    build: function (type, opts) {
      var m = buildModel(type, opts);                                  // all 16 builders return { orientation, blocks:[...] }
      return Promise.resolve(renderDoc(m.blocks, m.orientation).output('blob'));
    },
    /* BATCH salary slips (native vector, no screenshots). mode 'combined' = one PDF, one slip per page;
       mode 'zip' = one PDF per employee in a ZIP. `slips` is the array of gmSlipData objects from the
       payroll module (exact figures). buildBatch returns the Blob; batchSlips also delivers it. */
    buildBatch: function (mode, slips) {
      slips = Array.isArray(slips) ? slips : [];
      var libs = (window.__SaagarPdfLibs && window.__SaagarPdfLibs.ensure) ? window.__SaagarPdfLibs.ensure() : Promise.resolve();
      return libs.then(function () {
        if (mode === 'combined') {
          var blocks = [{ t: 'header', title: 'SALARY SLIPS', sub: 'Saagar Traders · Latur', period: 'For ' + ((slips[0] && slips[0].period) || '') }];
          slips.forEach(function (s, i) {
            blocks = blocks.concat(slipBlocks(s, (s.name || '—') + ' · ' + (s.empId || '')));
            if (i < slips.length - 1) blocks.push({ t: 'pagebreak' });
          });
          return renderDoc(blocks, 'portrait').output('blob');
        }
        return ensureZip().then(function () {
          var zip = new window.JSZip();
          slips.forEach(function (s, i) {
            var doc = renderDoc([{ t: 'header', title: 'SALARY SLIP', sub: 'Saagar Traders · Latur', period: 'For ' + (s.period || '') }].concat(slipBlocks(s, 'Employee & Attendance')), 'portrait');
            var nm = String(i + 1).padStart(3, '0') + '_' + String(s.empId || ('GM' + (i + 1))).replace(/[^A-Za-z0-9]+/g, '') + '_' + String(s.name || '').replace(/[^A-Za-z0-9]+/g, '_').slice(0, 40) + '.pdf';
            zip.file(nm, doc.output('blob'));
          });
          return zip.generateAsync({ type: 'blob', compression: 'DEFLATE', compressionOptions: { level: 5 } });
        });
      });
    },
    batchSlips: function (mode, slips, fileBase) {
      slips = Array.isArray(slips) ? slips : [];
      if (!slips.length) { try { toast('No employees selected.'); } catch (e) {} return Promise.resolve(); }
      try { toast('Preparing ' + slips.length + ' slip(s)…'); } catch (e) {}
      var base = fileBase || 'Salary_Slips';
      return this.buildBatch(mode, slips)
        .then(async function (blob) {
          var fname = base + (mode === 'zip' ? '.zip' : '.pdf');
          var token = await authorizeExport({
            exportId: 'payroll-slip-batch-share', kind: mode === 'zip' ? 'zip' : 'pdf',
            scopeId: 'payroll-slip-batch', scopeLabel: 'salary-slip batch', module: 'payroll',
            rowCount: slips.length, fileName: fname, purposeId: 'payroll-delivery'
          });
          return token ? shareBlob(blob, fname, token) : null;
        })
        .catch(function (e) { try { toast('Could not build slips: ' + ((e && e.message) || e)); } catch (_) {} });
    },
    generate: function (type, opts) {
      try { toast('Preparing report…'); } catch (e) {}
      var self = this;
      return new Promise(function (res) { setTimeout(res, 40); }).then(function () {
        return self.build(type, opts).then(async function (blob) {
          var fname = filename(type, opts), token = await authorizeExport({
            exportId: 'report-generate-share', kind: 'pdf', scopeId: 'report-' + type,
            scopeLabel: (META[type] ? META[type].title : type) + ' report', module: (META[type] && META[type].module) || 'reports',
            rowCount: reportRowCount(type, opts), fileName: fname, purposeId: 'management-reporting'
          });
          return token ? shareBlob(blob, fname, token) : null;
        });
      }).catch(function (e) { try { toast('Could not build report: ' + (e && e.message || e)); } catch (_) {} });
    },
    /* ── REPORT PREVIEW (exact PDF via pdf.js) + Save / Send / Print. Generation (build/buildModel/renderDoc)
       is unchanged — this only renders the same blob on-screen and adds save/print delivery. ── */
    preview: function (type, opts) {
      var self = this, el = function (i) { return document.getElementById(i); };
      opts = opts || {};
      // Salary slip needs ONE employee. fromHub('payrollSlip') passes no empId/empIndex,
      // so without a pick we'd silently emit the first employee — instead show a chooser
      // and let the tap re-enter preview() with the chosen empId.
      if (type === 'payrollSlip' && !opts.slip && !opts.empId && opts.empIndex == null) {
        return this._payrollChooser(opts);
      }
      self._pp = { type: type, opts: opts, blob: null, fname: filename(type, opts) };
      try { self._logReport(type, 'view', opts); } catch (e) {}
      var title = (META[type] ? META[type].title : 'Report');
      if (el('modalTitle')) el('modalTitle').textContent = title + ' — preview';
      if (el('modalBody')) el('modalBody').innerHTML =
        '<div class="pp-wrap"><div class="pp-scroll" id="ppScroll"><div class="pp-status">Building report…</div></div>'
        + '<div class="pp-bar">'
        + '<button class="btn small" type="button" onclick="SaagarReport.printPreview()">🖨️ Print</button>'
        + '<button class="btn small" type="button" onclick="SaagarReport.savePreview()">💾 Save</button>'
        + (CSV_REPORTS[type] ? '<button class="btn small" type="button" onclick="SaagarReport.savePreviewCsv()">CSV</button>' : '')
        + '<button class="btn small primary" type="button" onclick="SaagarReport.sharePreview()">Send ↗</button>'
        + '</div></div>';
      var mc = document.querySelector('.modal'); if (mc) { mc.classList.remove('hub-sheet'); mc.classList.add('pdf-sheet'); }
      if (window.openModal) window.openModal();
      this.build(type, opts).then(function (blob) {
        self._pp.blob = blob; self._renderPdf(blob, type, opts);
      }).catch(function (e) {
        var s = el('ppScroll'); if (s) s.innerHTML = '<div class="pp-status">Could not build report: ' + esc((e && e.message) || e) + '</div>';
      });
    },
    /* Salary-slip employee chooser — rendered inside the preview modal when no employee
       was specified. Picking a row re-enters preview('payrollSlip', {…opts, empId}). */
    _payrollChooser: function (opts) {
      var self = this, el = function (i) { return document.getElementById(i); };
      var rows = [];
      try { var p = JSON.parse(localStorage.getItem('payroll_suite_v1_2026')); rows = (p && Array.isArray(p.rows)) ? p.rows : []; } catch (e) {}
      self._ppPick = opts;   // forward the (date/month/etc.) opts to the chosen-employee preview
      if (el('modalTitle')) el('modalTitle').textContent = 'Salary Slip — choose employee';
      var listHtml = !rows.length
        ? '<p class="hub-help">No employees found in this month’s payroll.</p>'
        : '<div class="hub-sec" style="margin-top:6px">' + rows.map(function (r, i) {
            var key = (r && r.empId != null && r.empId !== '') ? String(r.empId) : '';
            var arg = key ? ("'" + key.replace(/'/g, "\\'") + "'") : i;   // empId when present, else row index
            return '<div class="hub-row" role="button" tabindex="0" onclick="SaagarReport._payrollPick(' + arg + ')">'
              + '<span class="ico">🧾</span>'
              + '<span class="ttl">' + esc((r && r.name) || 'Employee ' + (i + 1)) + '</span>'
              + '<span class="go" style="color:var(--muted)">' + esc((r && r.empId) || '—') + '</span></div>';
          }).join('') + '</div><p class="hub-help">Tap an employee to build their salary slip.</p>';
      if (el('modalBody')) el('modalBody').innerHTML = '<div class="pp-wrap"><div class="pp-scroll" id="ppScroll">' + listHtml + '</div></div>';
      var mc = document.querySelector('.modal'); if (mc) { mc.classList.remove('hub-sheet'); mc.classList.add('pdf-sheet'); }
      if (window.openModal) window.openModal();
    },
    _payrollPick: function (sel) {
      var opts = this._ppPick || {};
      var next = (typeof sel === 'number') ? { empIndex: sel } : { empId: sel };
      var merged = {}; for (var k in opts) { if (Object.prototype.hasOwnProperty.call(opts, k)) merged[k] = opts[k]; }
      merged.empIndex = next.empIndex; merged.empId = next.empId;
      return this.preview('payrollSlip', merged);
    },
    _renderPdf: function (blob, type, opts) {
      var scroll = document.getElementById('ppScroll'); if (!scroll) return;
      var note = this._readinessNote(type, opts);
      if (!window.pdfjsLib || !pdfjsLib.getDocument) { scroll.innerHTML = (note ? '<div class="pp-note">' + esc(note) + '</div>' : '') + '<div class="pp-status">Preview not available on this device — use Save or Send to get the PDF.</div>'; return; }
      var url = URL.createObjectURL(blob);
      function paint(pdf) {
        scroll.innerHTML = note ? '<div class="pp-note">' + esc(note) + '</div>' : '';
        var n = pdf.numPages;
        function page(i) {
          pdf.getPage(i).then(function (pg) {
            var dpr = window.devicePixelRatio || 1;
            var base = pg.getViewport({ scale: 1 });
            var targetW = Math.min(720, (scroll.clientWidth || 360) - 28);
            var v = pg.getViewport({ scale: (targetW / base.width) * dpr });
            var c = document.createElement('canvas'); c.width = v.width; c.height = v.height; c.style.width = (v.width / dpr) + 'px';
            scroll.appendChild(c);
            pg.render({ canvasContext: c.getContext('2d'), viewport: v }).promise.then(function () {
              if (i < n) page(i + 1); else { try { URL.revokeObjectURL(url); } catch (e) {} }
            });
          });
        }
        page(1);
      }
      function fail(e) {
        scroll.innerHTML = (note ? '<div class="pp-note">' + esc(note) + '</div>' : '') + '<div class="pp-status">Preview failed (' + esc((e && e.message) || e) + ') — use Save or Send.</div>';
        try { URL.revokeObjectURL(url); } catch (_) {}
      }
      // Try the normal web-worker path first; if the worker can't start in the
      // offline WebView, retry once on the main thread before giving up — so the
      // preview (and the Print that renders these canvases) works regardless of
      // how the device handles the pdf.js worker.
      pdfjsLib.getDocument({ url: url }).promise.then(paint).catch(function () {
        try { pdfjsLib.getDocument({ url: url, disableWorker: true }).promise.then(paint).catch(fail); }
        catch (e2) { fail(e2); }
      });
    },
    savePreview: async function () {
      var p = this._pp; if (!p || !p.blob) return;
      var token = await authorizeExport({
        exportId: 'report-preview-save', kind: 'pdf', scopeId: 'report-' + p.type,
        scopeLabel: (META[p.type] ? META[p.type].title : p.type) + ' report', module: 'reports',
        rowCount: reportRowCount(p.type, p.opts), fileName: p.fname, purposeId: 'management-reporting'
      });
      if (!token) return;
      try { this._logReport(p.type, 'save', p.opts); } catch (e) {}
      return this._saveBlob(p.blob, p.fname, token);
    },
    savePreviewCsv: async function () {
      var p = this._pp; if (!p || !CSV_REPORTS[p.type]) return;
      try {
        var artifact = modelToCsv(p.type, p.opts), fname = p.fname.replace(/\.pdf$/i, '.csv');
        var token = await authorizeExport({
          exportId: 'report-preview-csv', kind: 'csv', scopeId: 'report-' + p.type,
          scopeLabel: (META[p.type] ? META[p.type].title : p.type) + ' CSV', module: 'reports',
          rowCount: artifact.rowCount, fileName: fname, purposeId: 'register-reporting'
        });
        if (!token) return;
        try { this._logReport(p.type, 'save-csv', p.opts); } catch (e) {}
        return this._saveBlob(artifact.blob, fname, token);
      } catch (e) { try { toast('CSV could not be created: ' + ((e && e.message) || e)); } catch (_) {} }
    },
    sharePreview: async function () {
      var p = this._pp; if (!p || !p.blob) return;
      var token = await authorizeExport({
        exportId: 'report-preview-share', kind: 'pdf', scopeId: 'report-' + p.type,
        scopeLabel: (META[p.type] ? META[p.type].title : p.type) + ' report', module: 'reports',
        rowCount: reportRowCount(p.type, p.opts), fileName: p.fname, purposeId: 'management-reporting'
      });
      if (!token) return;
      try { this._logReport(p.type, 'send', p.opts); } catch (e) {}
      try { var mc = document.querySelector('.modal'); if (mc) mc.classList.remove('pdf-sheet'); if (window.closeModal) window.closeModal(); } catch (e) {}
      return shareBlob(p.blob, p.fname, token);
    },
    printPreview: async function () {
      var p = this._pp || {}, token = await authorizeExport({
        exportId: 'report-preview-print', kind: 'print', scopeId: 'report-' + (p.type || 'unknown'),
        scopeLabel: (META[p.type] ? META[p.type].title : 'report') + ' print', module: 'reports',
        rowCount: p.type ? reportRowCount(p.type, p.opts) : 0, purposeId: 'hard-copy-business-record'
      });
      if (!token) return;
      if (!beginExport(token)) return;
      try { window.print(); finishExport(token, 'printed'); } catch (e) { finishExport(token, 'failed'); }
    },
    _saveBlob: function (blob, fname, token) {
      if (!beginExport(token)) {
        try { toast('Report save blocked — approval token is no longer valid.'); } catch (e) {}
        return Promise.resolve({ blocked: true });
      }
      var c = (function () { try { return capsShare(); } catch (e) { return null; } })();
      if (c && c.FS) {
        return new Promise(function (res) { var fr = new FileReader(); fr.onloadend = function () { res(String(fr.result).split(',')[1]); }; fr.readAsDataURL(blob); })
          .then(function (b64) { return c.FS.writeFile({ path: 'SaagarBCC-Reports/' + fname, data: b64, directory: 'DOCUMENTS', recursive: true }); })
          .then(function () { finishExport(token, 'downloaded'); try { toast('Saved to Documents › SaagarBCC-Reports › ' + fname); } catch (e) {} })
          // No file-opener plugin offline — hand the saved file to the OS share sheet
          // so the user can open it in their PDF viewer (or share it onward).
          .then(function () { return c.FS.getUri({ directory: 'DOCUMENTS', path: 'SaagarBCC-Reports/' + fname }); })
          .then(function (r) { return c.Share.share({ title: fname, text: fname, files: [r.uri], dialogTitle: 'Open or share the PDF' }); })
          .catch(function (e) { var m = (e && e.message) || String(e); if (!/cancel/i.test(m)) { finishExport(token, 'failed'); try { toast('Save failed: ' + m); } catch (_) {} } });
      }
      var a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = fname; a.click();
      setTimeout(function () { URL.revokeObjectURL(a.href); }, 1500);
      finishExport(token, 'downloaded');
      try { toast('Report downloaded'); } catch (e) {}
      return Promise.resolve();
    },
    _readinessNote: function (type, opts) {
      try {
        var d = (opts && opts.date) || curDate();
        if (type === 'cashStatement') { var st = (JSON.parse(localStorage.getItem('tanishq_statements') || '{}'))[d]; if (!st) return 'No cash statement for this day yet — the report may be blank.'; if (!st.closed) return 'This day’s cash is not closed yet — figures may be provisional.'; }
        if (type === 'dsrRegister') { var any = false; for (var i = 0; i < localStorage.length; i++) { if ((localStorage.key(i) || '').indexOf('saagar_dsr_' + d) === 0) { any = true; break; } } if (!any) return 'No staff register entries for this day yet.'; }
      } catch (e) {}
      return '';
    },
    /* ── BATCH PACKS — build a whole period's reports at once, then Save-all / Send-all.
       Reuses build() per report (generation unchanged); just bundles delivery. ── */
    _packTypes: { daily: ['ownerBrief', 'cashStatement', 'dsrRegister', 'qmsReport', 'croAudit', 'groomingDaily', 'stockRegister', 'serviceAging'], monthly: ['ownerMonthly', 'payrollRegister', 'statutorySummary', 'leaveRegister', 'groomingMonthly', 'expenseMonthly', 'taxReport'] },
    pack: function (scope) {
      var self = this, el = function (i) { return document.getElementById(i); };
      var d = (el('rptDate') && el('rptDate').value) || curDate();
      var m = (el('rptMonth') && el('rptMonth').value) || curMonth();
      var types = this._packTypes[scope === 'monthly' ? 'monthly' : 'daily'];
      var label = (scope === 'monthly') ? ('Month-end pack · ' + m) : ('End-of-day pack · ' + d);
      if (el('modalTitle')) el('modalTitle').textContent = label;
      if (el('modalBody')) el('modalBody').innerHTML = '<div class="pp-wrap"><div class="pp-scroll" id="ppScroll"><div class="pp-status">Building ' + types.length + ' reports…</div></div>'
        + '<div class="pp-bar"><button class="btn small" type="button" onclick="SaagarReport.packSave()">💾 Save all</button><button class="btn small primary" type="button" onclick="SaagarReport.packSend()">Send all ↗</button></div></div>';
      var mc = document.querySelector('.modal'); if (mc) { mc.classList.remove('hub-sheet'); mc.classList.add('pdf-sheet'); }
      if (window.openModal) window.openModal();
      self._pack = { items: [], scope: scope, d: d, m: m };
      var i = 0;
      function next() {
        if (i >= types.length) { self._renderPackList(); return; }
        var t = types[i++];
        self.build(t, { date: d, month: m }).then(function (blob) {
          self._pack.items.push({ type: t, title: (META[t] ? META[t].title : t), blob: blob, fname: filename(t, { date: d, month: m }) });
          var s = el('ppScroll') && el('ppScroll').querySelector('.pp-status'); if (s) s.textContent = 'Building ' + i + ' of ' + types.length + '…';
          next();
        }).catch(function () { next(); });
      }
      next();
    },
    _renderPackList: function () {
      var scroll = document.getElementById('ppScroll'); if (!scroll) return;
      var items = (this._pack && this._pack.items) || [];
      scroll.innerHTML = '<div class="hub-sec" style="margin-top:6px">' + items.map(function (it) {
        return '<div class="hub-row" style="cursor:default"><span class="ico">' + (META[it.type] ? META[it.type].icon : '📄') + '</span><span class="ttl">' + esc(it.title) + '</span><span class="go" style="color:var(--green)">✓</span></div>';
      }).join('') + '</div><p class="hub-help">' + items.length + ' reports ready. <b>Save all</b> writes them to your phone, or <b>Send all</b> shares them together on WhatsApp.</p>';
      this._logReport('pack_' + (this._pack ? this._pack.scope : ''), 'view', { date: this._pack && this._pack.d, month: this._pack && this._pack.m });
    },
    packSave: async function () {
      var items = (this._pack && this._pack.items) || []; if (!items.length) return Promise.resolve();
      var token = await authorizeExport({
        exportId: 'report-pack-save', kind: 'pdf', scopeId: 'report-pack-' + ((this._pack && this._pack.scope) || 'period'),
        scopeLabel: 'report pack', module: 'reports', rowCount: items.length, fileName: 'Saagar-report-pack.pdf',
        purposeId: 'management-reporting'
      });
      if (!token) return Promise.resolve();
      if (!beginExport(token)) return Promise.resolve({ blocked: true });
      var c = (function () { try { return capsShare(); } catch (e) { return null; } })();
      if (!(c && c.FS)) { items.forEach(function (it) { var a = document.createElement('a'); a.href = URL.createObjectURL(it.blob); a.download = it.fname; a.click(); }); finishExport(token, 'downloaded'); try { toast(items.length + ' reports downloaded'); } catch (e) {} return Promise.resolve(); }
      var n = 0;
      return items.reduce(function (p, it) {
        return p.then(function () {
          return new Promise(function (res) { var fr = new FileReader(); fr.onloadend = function () { res(String(fr.result).split(',')[1]); }; fr.readAsDataURL(it.blob); })
            .then(function (b64) { return c.FS.writeFile({ path: 'SaagarBCC-Reports/' + it.fname, data: b64, directory: 'DOCUMENTS', recursive: true }); })
            .then(function () { n++; });
        });
      }, Promise.resolve()).then(function () { finishExport(token, 'downloaded'); try { toast('Saved ' + n + ' reports to Documents › SaagarBCC-Reports'); } catch (e) {} })
        .catch(function (e) { finishExport(token, 'failed'); throw e; });
    },
    packSend: async function () {
      var items = (this._pack && this._pack.items) || []; if (!items.length) return Promise.resolve();
      var token = await authorizeExport({
        exportId: 'report-pack-share', kind: 'pdf', scopeId: 'report-pack-' + ((this._pack && this._pack.scope) || 'period'),
        scopeLabel: 'report pack', module: 'reports', rowCount: items.length, fileName: 'Saagar-report-pack.pdf',
        purposeId: 'management-reporting'
      });
      if (!token) return Promise.resolve();
      if (!beginExport(token)) return Promise.resolve({ blocked: true });
      var c = (function () { try { return capsShare(); } catch (e) { return null; } })();
      if (!(c && c.Share && c.FS)) {
        items.forEach(function (it) { var a = document.createElement('a'); a.href = URL.createObjectURL(it.blob); a.download = it.fname; a.click(); });
        finishExport(token, 'downloaded');
        try { toast(items.length + ' reports downloaded'); } catch (e) {}
        return Promise.resolve();
      }
      var uris = [];
      return items.reduce(function (p, it) {
        return p.then(function () {
          return new Promise(function (res) { var fr = new FileReader(); fr.onloadend = function () { res(String(fr.result).split(',')[1]); }; fr.readAsDataURL(it.blob); })
            .then(function (b64) { return c.FS.writeFile({ path: it.fname, data: b64, directory: 'CACHE' }); })
            .then(function () { return c.FS.getUri({ directory: 'CACHE', path: it.fname }); })
            .then(function (r) { uris.push(r.uri); });
        });
      }, Promise.resolve()).then(function () {
        try { var mc = document.querySelector('.modal'); if (mc) mc.classList.remove('pdf-sheet'); if (window.closeModal) window.closeModal(); } catch (e) {}
        return c.Share.share({ title: 'Saagar Traders reports', text: 'Saagar Traders — report pack', files: uris, dialogTitle: 'Share reports' });
      }).then(function () { finishExport(token, 'shared'); })
        .catch(function (e) { var msg = (e && e.message) || String(e), cancelled = /cancel|dismiss/i.test(msg); finishExport(token, cancelled ? 'cancelled' : 'failed'); if (!cancelled) { try { toast('Share failed: ' + msg); } catch (_) {} } });
    },
    /* ── REPORT HISTORY — a log of what was generated/sent and when, with one-tap re-open. ── */
    _logReport: function (type, action, opts) {
      try {
        var log = JSON.parse(localStorage.getItem('saagar_rpt_log') || '[]') || [];
        log.unshift({ t: type, a: action, ts: Date.now(), d: (opts && opts.date) || null, m: (opts && opts.month) || null });
        localStorage.setItem('saagar_rpt_log', JSON.stringify(log.slice(0, 40)));
      } catch (e) {}
    },
    history: function () {
      var el = function (i) { return document.getElementById(i); };
      var log = []; try { log = JSON.parse(localStorage.getItem('saagar_rpt_log') || '[]') || []; } catch (e) {}
      function rel(ts) { var s = Math.max(0, (Date.now() - ts) / 1000); if (s < 60) return 'just now'; if (s < 3600) return Math.floor(s / 60) + 'm ago'; if (s < 86400) return Math.floor(s / 3600) + 'h ago'; return Math.floor(s / 86400) + 'd ago'; }
      var ACT = { view: 'opened', save: 'saved', send: 'sent' };
      var rows = log.filter(function (e) { return META[e.t]; }).slice(0, 25).map(function (e) {
        var d = e.d || '', m = e.m || '';
        return '<button class="hub-row" type="button" onclick="SaagarReport.preview(\'' + e.t + '\',{date:\'' + d + '\',month:\'' + m + '\'})">'
          + '<span class="ico">' + META[e.t].icon + '</span><span class="ttl">' + esc(META[e.t].title) + '<small>' + (ACT[e.a] || e.a) + ' · ' + rel(e.ts) + (d ? ' · ' + d : '') + '</small></span><span class="go">↗</span></button>';
      }).join('');
      if (el('modalTitle')) el('modalTitle').textContent = 'Report history';
      if (el('modalBody')) el('modalBody').innerHTML = '<div class="hub-wrap"><div class="hub-list">'
        + (rows ? '<section class="hub-sec">' + rows + '</section>' : '<div class="hub-empty" style="display:block">No reports generated yet — open one from the report list first.</div>')
        + '</div></div>';
      var mc = document.querySelector('.modal'); if (mc) { mc.classList.remove('pdf-sheet'); mc.classList.add('hub-sheet'); }
      if (window.openModal) window.openModal();
    },
    list: function () { return Object.keys(META).map(function (k) { return { type: k, title: META[k].title, scope: META[k].scope, icon: META[k].icon }; }); },
    forModule: function (id) { return ({ qms: 'qmsReport', dsr: 'dsrRegister', cro_audit: 'croAudit', payroll: 'payrollRegister', stock: 'stockRegister', expense: 'cashStatement', tax: 'taxReport', service: 'serviceAging', grooming: 'groomingDaily', leave: 'leaveRegister' })[id] || null; },
    /* ── REDESIGN "Find & Send": full-screen thumb sheet, search, recents, period stepper.
       Selection/presentation only — META, generate(), buildModel(), renderDoc() are untouched. ── */
    openHub: function () {
      var el = function (i) { return document.getElementById(i); };
      var GROUPS = [
        { h: 'Daily · Owner',                types: ['ownerBrief', 'cashStatement', 'dsrRegister'] },
        { h: 'Daily · Floor & Service',      types: ['qmsReport', 'croAudit', 'groomingDaily', 'stockRegister', 'serviceAging', 'serviceJobCard'] },
        { h: 'Weekly · Owner',               types: ['weeklyBrief'] },
        { h: 'Monthly · People',             types: ['payrollRegister', 'payrollSlip', 'leaveRegister', 'groomingMonthly', 'lostWalkinCallSheet'] },
        { h: 'Monthly · Money & Compliance', types: ['expenseMonthly', 'taxReport', 'ownerMonthly'] }
      ];
      var TAGS = {
        ownerBrief: 'owner brief summary digest day', cashStatement: 'cash money statement till',
        dsrRegister: 'dsr sales register daily', qmsReport: 'queue conversion footfall walkin qms',
        croAudit: 'cro audit scorecard checklist', stockRegister: 'stock closing inventory grn',
        groomingDaily: 'grooming daily audit appearance', serviceAging: 'service open cases aging sla repair',
        serviceJobCard: 'service job card repair watch', payrollRegister: 'payroll salary register wages',
        payrollSlip: 'payroll salary slip payslip', leaveRegister: 'leave absent holiday register',
        groomingMonthly: 'grooming monthly trend', expenseMonthly: 'expense pnl profit loss money',
        taxReport: 'tax compliance gst tds due', ownerMonthly: 'owner monthly brief summary',
        weeklyBrief: 'weekly business summary owner week wtd trend last week comparison',
        lostWalkinCallSheet: 'lost walk-in call sheet follow-up non purchase callback missed customers qms'
      };
      function row(t, recent) {
        var m = META[t]; if (!m) return '';
        return '<button class="hub-row' + (recent ? ' recent' : '') + '" data-type="' + t + '" data-tags="' + esc(m.title.toLowerCase() + ' ' + (TAGS[t] || '')) + '" '
          + 'onclick="SaagarReport.fromHub(\'' + t + '\')">'
          + '<span class="ico">' + m.icon + '</span>'
          + '<span class="ttl">' + esc(m.title) + '</span>'
          + '<span class="scope">' + (m.scope === 'monthly' ? 'Monthly' : (m.scope === 'weekly' ? 'Weekly' : 'Daily')) + '</span>'
          + '<span class="go">↗</span></button>';
      }
      var recents = [];
      try { recents = JSON.parse(localStorage.getItem('saagar_rpt_recent') || '[]'); } catch (e) {}
      recents = (recents || []).filter(function (t) { return META[t]; }).slice(0, 3);
      var recentHtml = recents.length
        ? '<section class="hub-sec" data-recent><h4 class="hub-sec-h">Recent</h4>' + recents.map(function (t) { return row(t, true); }).join('') + '</section>'
        : '';
      var groupHtml = GROUPS.map(function (g) {
        return '<section class="hub-sec"><h4 class="hub-sec-h">' + esc(g.h) + '</h4>'
          + g.types.map(function (t) { return row(t, false); }).join('') + '</section>';
      }).join('');
      var packHtml = '<section class="hub-sec hub-packs">'
        + '<button class="hub-pack" type="button" onclick="SaagarReport.pack(\'daily\')"><span class="ico">📦</span><span class="ttl">End-of-day pack<small>All daily reports for the date</small></span><span class="go">→</span></button>'
        + '<button class="hub-pack" type="button" onclick="SaagarReport.pack(\'monthly\')"><span class="ico">📦</span><span class="ttl">Month-end pack<small>All monthly reports</small></span><span class="go">→</span></button>'
        + '<button class="hub-hist" type="button" onclick="SaagarReport.history()">🕘 Report history</button>'
        + '</section>';
      var body = '<div class="hub-wrap">'
        + '<div class="hub-list" id="hubList">'
        + packHtml + recentHtml + groupHtml
        + '<div class="hub-empty" id="hubEmpty" hidden>No report matches that search.</div>'
        + '<p class="hub-help">Tap a report → it builds a clean A4 PDF and opens the share sheet. Pick WhatsApp + the contact.</p>'
        + '</div>'
        + '<div class="hub-bar">'
        + '<div class="rpt-count" id="hubCount">' + (Object.keys(META).length) + ' reports</div>'
        + '<div class="rpt-period">'
        + '<button class="rpt-step" type="button" onclick="SaagarReport.hubStep(-1)" aria-label="Previous">‹</button>'
        + '<div class="valwrap"><div class="val" id="hubPeriodLabel"></div>'
        + '<input id="rptDate" type="date" value="' + curDate() + '">'
        + '<input id="rptMonth" type="month" value="' + curMonth() + '" style="display:none"></div>'
        + '<button class="rpt-step" type="button" onclick="SaagarReport.hubStep(1)" aria-label="Next">›</button>'
        + '</div>'
        + '<label class="rpt-search"><span class="ic">🔍</span>'
        + '<input id="hubSearch" type="text" inputmode="search" placeholder="Search reports — try cash, tax, payroll" oninput="SaagarReport.hubFilter(this.value)"></label>'
        + '</div></div>';
      var mc = document.querySelector('.modal'); if (mc) mc.classList.add('hub-sheet');
      if (el('modalTitle')) el('modalTitle').textContent = 'Generate Report (PDF)';
      if (el('modalBody')) el('modalBody').innerHTML = body;
      if (window.openModal) window.openModal();
      this.hubSyncLabel();
    },
    hubStep: function (delta) {
      var d = document.getElementById('rptDate'); if (!d || !d.value) return;
      var base = new Date(d.value + 'T12:00:00'); base.setDate(base.getDate() + delta);
      var max = new Date(curDate() + 'T12:00:00'); if (base > max) base = max;
      d.value = base.toISOString().slice(0, 10);
      var mo = document.getElementById('rptMonth'); if (mo) mo.value = d.value.slice(0, 7);
      this.hubSyncLabel();
    },
    hubSyncLabel: function () {
      var d = document.getElementById('rptDate'), lab = document.getElementById('hubPeriodLabel');
      if (!d || !lab) return;
      var dt = new Date(d.value + 'T12:00:00');
      lab.textContent = isNaN(dt) ? d.value : dt.toLocaleDateString('en-IN', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
    },
    hubFilter: function (q) {
      q = (q || '').trim().toLowerCase();
      var shown = 0;
      document.querySelectorAll('#hubList .hub-row').forEach(function (r) {
        var hay = (r.getAttribute('data-tags') || '') + ' ' + r.textContent.toLowerCase();
        var hit = !q || hay.indexOf(q) >= 0;
        r.style.display = hit ? '' : 'none';
        if (hit && !r.classList.contains('recent')) shown++;
      });
      document.querySelectorAll('#hubList .hub-sec').forEach(function (sec) {
        if (!sec.querySelector('.hub-row')) return;   // keep non-report sections (packs) always visible
        sec.style.display = sec.querySelector('.hub-row:not([style*="display: none"])') ? '' : 'none';
      });
      var empty = document.getElementById('hubEmpty'); if (empty) empty.hidden = shown > 0;
      var cnt = document.getElementById('hubCount');
      if (cnt) cnt.textContent = q ? (shown + ' of ' + Object.keys(META).length) : (Object.keys(META).length + ' reports');
    },
    fromHub: function (type) {
      var el = function (i) { return document.getElementById(i); };
      var d = (el('rptDate') && el('rptDate').value) || curDate(), m = (el('rptMonth') && el('rptMonth').value) || curMonth();
      try {
        var arr = JSON.parse(localStorage.getItem('saagar_rpt_recent') || '[]') || [];
        arr = [type].concat(arr.filter(function (x) { return x !== type; })).slice(0, 3);
        localStorage.setItem('saagar_rpt_recent', JSON.stringify(arr));
      } catch (e) {}
      return this.preview(type, { date: d, month: m });   // tap → PREVIEW (then Save / Send / Print)
    },
    _buildModel: buildModel, // for tests
    _blocksToCsv: blocksToCsv,
    _modelToCsv: modelToCsv,
    _modelRowCount: modelRowCount,
    _renderDoc: function (type, opts) { var m = buildModel(type, opts); if (!m.blocks) throw new Error('not block-based: ' + type); return renderDoc(m.blocks, m.orientation); }, // test seam → jsPDF doc
    _renderBlocks: function (blocks, orientation) { return renderDoc(blocks, orientation); } // test seam → raw block list
  };
})();
