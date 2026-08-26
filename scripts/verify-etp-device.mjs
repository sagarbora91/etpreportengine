#!/usr/bin/env node
import fs from 'node:fs';
const endpoint = process.argv[2];
if (!endpoint) throw new Error('WebSocket endpoint required');
const socket = new WebSocket(endpoint);
let sequence = 0;
const pending = new Map();
socket.onmessage = event => {
  const message = JSON.parse(event.data);
  if (message.id && pending.has(message.id)) {
    pending.get(message.id)(message);
    pending.delete(message.id);
  }
};
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
function call(method, params = {}) {
  return new Promise(resolve => {
    const id = ++sequence;
    pending.set(id, resolve);
    socket.send(JSON.stringify({ id, method, params }));
  });
}
if (!process.argv.includes('--no-open')) {
  await call('Runtime.evaluate', { expression: "openModule('etp')", awaitPromise: true });
  await new Promise(resolve => setTimeout(resolve, 1200));
}
if (process.argv.includes('--verified')) {
  await call('Runtime.evaluate', { expression: "document.querySelector('iframe').contentDocument.querySelector('[data-view=verified]').click()" });
  await new Promise(resolve => setTimeout(resolve, 1500));
}
const inspected = await call('Runtime.evaluate', {
  expression: `(async () => {
    const frame = document.querySelector('iframe');
    if (!frame || !frame.contentDocument) return { error: 'ETP_FRAME_UNAVAILABLE' };
    const doc = frame.contentDocument;
    const financialYear = doc.querySelector('[data-etp-scope="financialYear"]');
    const files = Array.from(doc.querySelectorAll('[data-etp-file]'));
    const gateway = window.SaagarEtpModuleGateway;
    let liveDiagnostics = null;
    if (gateway && gateway.readFacade) {
      const listed = await gateway.readFacade.listScopes({ limit: 20 });
      const scopes = listed && listed.ok && listed.scopes || [];
      const selectedItem = scopes.find(item => item && item.scope && doc.querySelector('[data-etp-active-scope="verified"]').textContent.indexOf(item.scope.periodStart + ' to ' + item.scope.periodEnd) >= 0) || scopes[0];
      const selected = selectedItem && selectedItem.scope;
      const projections = {
        R003: ['invoice_date','transaction_type_raw','net_amount','scheme_discount','user_discount'],
        R013: ['invoice_date','transaction_type_raw','quantity','net_amount','cro_number'],
        R022: ['invoice_date','invoice_number','transaction_type_raw','invoice_quantity','net_value','cash_amount','card_amount','bhim_upi_amount','phonepe_amount','paytm_amount','razorpay_amount','bharatpe_amount','cheque_amount','others_amount','payment_type24_amount'],
        R025: ['invoice_date','invoice_number','transaction_type_raw','quantity','net_amount','brand','cluster','gender','scheme_discount','user_discount','tax_amount']
      };
      const reads = {};
      if (selected) for (const id of Object.keys(projections)) {
        const result = await gateway.readVerified(selected, { reportId: id, fields: projections[id], cursor: null, limit: 1 });
        reads[id] = { ok: result && result.ok === true, code: result && result.code || null };
      }
      const analytics = selected ? await gateway.readFacade.loadAnalytics(selected, { view: 'YTD', asOfDate: new Date().toISOString().slice(0, 10) }) : null;
      liveDiagnostics = { scopes: scopes.map(item => item && item.scope && ({ storeCode: item.scope.storeCode, financialYear: item.scope.financialYear, periodStart: item.scope.periodStart, periodEnd: item.scope.periodEnd })), selected: selected && { storeCode: selected.storeCode, financialYear: selected.financialYear, periodStart: selected.periodStart, periodEnd: selected.periodEnd }, reads, analytics: { ok: analytics && analytics.ok === true, code: analytics && analytics.code || null } };
    }
    return {
      frameSource: frame.getAttribute('src'),
      financialYearTag: financialYear && financialYear.tagName,
      financialYearOptions: financialYear && Array.from(financialYear.options).map(option => option.value),
      multiFileReports: files.filter(input => input.multiple).map(input => input.getAttribute('data-etp-file')),
      clientWidth: doc.documentElement.clientWidth,
      scrollWidth: doc.documentElement.scrollWidth,
      notice: doc.getElementById('etpImportNotice').textContent,
      liveDiagnostics
    };
  })()`,
  awaitPromise: true,
  returnByValue: true
});
process.stdout.write(`${JSON.stringify(inspected.result?.result?.value ?? {
  error: inspected.result?.exceptionDetails?.exception?.description || 'CDP_EVALUATION_FAILED',
  exceptionDetails: inspected.result?.exceptionDetails || null
}, null, 2)}\n`);
if (process.argv[3] && process.argv[3] !== '--open-picker') {
  const captured = await call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  fs.writeFileSync(process.argv[3], Buffer.from(captured.result.data, 'base64'));
}
if (process.argv.includes('--open-picker')) {
  await call('Runtime.evaluate', { expression: "document.querySelector('iframe').contentDocument.querySelector('[data-etp-file=R003]').click()" });
}
socket.close();
