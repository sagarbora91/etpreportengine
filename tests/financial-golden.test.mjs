import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { inlineModuleScripts, loadModuleBundle } from './lib/module-bundle.mjs';

const rootDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const modules = loadModuleBundle();

function memoryStorage(initial = {}) {
  const values = new Map(
    Object.entries(initial).map(([key, value]) => [
      key,
      typeof value === 'string' ? value : JSON.stringify(value)
    ])
  );
  return {
    getItem(key) {
      return values.has(String(key)) ? values.get(String(key)) : null;
    },
    setItem(key, value) {
      values.set(String(key), String(value));
    },
    removeItem(key) {
      values.delete(String(key));
    },
    clear() {
      values.clear();
    },
    key(index) {
      return [...values.keys()][index] ?? null;
    },
    get length() {
      return values.size;
    }
  };
}

function elementStub(overrides = {}) {
  return {
    value: '',
    textContent: '',
    innerHTML: '',
    checked: false,
    disabled: false,
    style: {},
    dataset: {},
    className: '',
    classList: {
      add() {},
      remove() {},
      toggle() {},
      contains() { return false; }
    },
    appendChild() {},
    remove() {},
    setAttribute() {},
    getAttribute() { return ''; },
    addEventListener() {},
    focus() {},
    select() {},
    ...overrides
  };
}

function browserContext({ storage = {}, elements = {} } = {}) {
  const localStorage = memoryStorage(storage);
  const nodes = new Map(
    Object.entries(elements).map(([id, value]) => [
      id,
      elementStub(typeof value === 'object' ? value : { value })
    ])
  );
  const defaultNode = elementStub();
  const document = {
    readyState: 'loading',
    body: elementStub(),
    documentElement: elementStub(),
    addEventListener() {},
    removeEventListener() {},
    createElement() { return elementStub(); },
    getElementById(id) { return nodes.get(String(id)) || defaultNode; },
    querySelector() { return null; },
    querySelectorAll() { return []; }
  };
  const sandbox = {
    localStorage,
    document,
    navigator: {},
    location: { href: 'https://offline.invalid/' },
    Blob,
    Date,
    Math,
    JSON,
    Number,
    String,
    Boolean,
    Array,
    Object,
    Map,
    Set,
    Promise,
    RegExp,
    TextEncoder,
    TextDecoder,
    URL: {
      createObjectURL() { return 'blob:offline'; },
      revokeObjectURL() {}
    },
    atob(value) { return Buffer.from(value, 'base64').toString('binary'); },
    btoa(value) { return Buffer.from(value, 'binary').toString('base64'); },
    alert() {},
    confirm() { return true; },
    prompt() { return ''; },
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent() { return true; },
    setTimeout,
    clearTimeout,
    setInterval,
    clearInterval
  };
  sandbox.window = sandbox;
  sandbox.parent = sandbox;
  sandbox.globalThis = sandbox;
  return vm.createContext(sandbox);
}

function evaluateModule(id, options) {
  const module = modules.find(item => item.id === id);
  assert.ok(module, `${id} module must be embedded`);
  const scripts = inlineModuleScripts(module.html);
  const source = scripts.find(script => {
    if (id === 'payroll') return script.includes('function calcGM');
    if (id === 'expense') return script.includes('function computeDay');
    if (id === 'leave') return script.includes('function fyUsage');
    return false;
  });
  assert.ok(source, `${id} calculation script must exist`);
  const context = browserContext(options);
  vm.runInContext(fs.readFileSync(path.join(rootDir, 'www/shared/module-bridge.js'), 'utf8'), context, {
    filename: 'shared/module-bridge.js'
  });
  vm.runInContext(fs.readFileSync(path.join(rootDir, 'www/shared/module-runtime.js'), 'utf8'), context, {
    filename: 'shared/module-runtime.js'
  });
  vm.runInContext(source, context, { filename: `${id}.embedded.html` });
  return context;
}

test('ENG-01 payroll golden case preserves approved gross-to-net and employer totals', () => {
  const context = evaluateModule('payroll');
  const actual = vm.runInContext(`
    state = {
      meta: {
        month: 'January',
        year: 2026,
        holidays: 0,
        totalDaysOverride: 30,
        rules: {
          pt: { maleExempt: 7500, maleMid: 10000, maleMidAmt: 175, stdAmt: 200, febAmt: 300, femaleExempt: 25000 },
          pf: { rateEE: 12, rateER: 13, wageCap: 15000 },
          esic: { rateEE: 0.75, rateER: 3.25, wageCeiling: 21000 }
        }
      },
      advances: [],
      runs: {}
    };
    calcGM({
      empId: 'E001',
      name: 'Golden Employee',
      firm: 'GM',
      gross: 20000,
      salaryType: 'S',
      gender: 'M',
      pfApplicable: true,
      esicApplicable: true,
      absent: 0,
      halfDay: 0,
      late: 0,
      noThumb: 0,
      leavesApplied: 0,
      advance: 0
    });
  `, context);

  const money = Object.fromEntries(
    ['grossPayable', 'basic', 'hra', 'washing', 'specAllow', 'pt', 'pfWages',
      'pfEE', 'esicEE', 'pfEmpr', 'esicEmpr', 'netPayable', 'totalCTC',
      'advance', 'finalPay'].map(key => [key, actual[key]])
  );
  assert.deepEqual(money, {
    grossPayable: 20000,
    basic: 10000,
    hra: 8000,
    washing: 1000,
    specAllow: 1000,
    pt: 200,
    pfWages: 10000,
    pfEE: 1200,
    esicEE: 142.5,
    pfEmpr: 1300,
    esicEmpr: 617.5,
    netPayable: 18457.5,
    totalCTC: 24584.166666666668,
    advance: 0,
    finalPay: 21124.166666666668
  });
  assert.equal(actual.totalSalaryDays, 30);
  assert.equal(actual.otDays, 4);
});

test('ENG-01 payroll PT boundaries and February amount stay fixed', () => {
  const context = evaluateModule('payroll');
  const slabs = vm.runInContext(`
    state = { meta: { rules: { pt: {
      maleExempt: 7500, maleMid: 10000, maleMidAmt: 175,
      stdAmt: 200, febAmt: 300, femaleExempt: 25000
    } } } };
    [
      ptFor(7500, 'M', 'January'),
      ptFor(7501, 'M', 'January'),
      ptFor(10001, 'M', 'February'),
      ptFor(25000, 'F', 'February'),
      ptFor(25001, 'F', 'February')
    ];
  `, context);
  assert.deepEqual([...slabs], [0, 175, 300, 0, 300]);
});

test('ENG-01 daily cash golden case carries the last closed drawer across date gaps', () => {
  const date = '2026-07-10';
  const context = evaluateModule('expense', {
    storage: {
      gm_expenses: [
        { id: 'i1', date, type: 'income', amount: 1000, mode: 'Cash', category: 'Retail Sale' },
        { id: 'e1', date, type: 'expense', amount: 200, mode: 'Cash', category: 'Transport' },
        { id: 'i2', date, type: 'income', amount: 300, mode: 'UPI', category: 'Service Income' },
        { id: 'e2', date, type: 'expense', amount: 50, mode: 'Bank', category: 'Bank Charges' },
        { id: 'void', date, type: 'expense', amount: 999, mode: 'Cash', void: true }
      ],
      tanishq_statements: {
        '2026-07-08': {
          date: '2026-07-08',
          closed: true,
          physDeno: { 500: 2 },
          bankDeno: { 200: 1 }
        }
      }
    }
  });
  const actual = vm.runInContext(`computeDay('${date}')`, context);

  assert.deepEqual(
    Object.fromEntries(
      ['opening', 'cashIn', 'cashOut', 'bankIn', 'bankOut', 'incTot', 'expTot',
        'net', 'expectedClosing', 'prevClosed'].map(key => [key, actual[key]])
    ),
    {
      opening: 800,
      cashIn: 1000,
      cashOut: 200,
      bankIn: 300,
      bankOut: 50,
      incTot: 1300,
      expTot: 250,
      net: 1050,
      expectedClosing: 1600,
      prevClosed: true
    }
  );
  assert.equal(actual.rows.length, 4);
});

test('ENG-01 GST feed golden case preserves per-category rates and input-tax total', () => {
  const context = evaluateModule('expense', {
    storage: {
      gm_role: 'owner',
      gm_settings: { gstRate: 18 },
      gm_gst_rates: { 'Retail Sale': 5 },
      gm_expenses: [
        { id: 'i1', date: '2026-01-05', type: 'income', amount: 10000, mode: 'Cash', category: 'Retail Sale' },
        { id: 'i2', date: '2026-01-06', type: 'income', amount: 1000, mode: 'UPI', category: 'Service Income' },
        { id: 'e1', date: '2026-01-07', type: 'expense', amount: 5000, mode: 'Bank', category: 'Inventory', gstAmount: 600 }
      ]
    }
  });
  vm.runInContext(`
    CM = '2026-01';
    renderClose = function(){};
    toast = function(){};
    genTaxFeed();
  `, context);
  const feed = JSON.parse(context.localStorage.getItem('gm_tax_feed'))['2026-01'];

  assert.equal(feed.income, 11000);
  assert.equal(feed.expense, 5000);
  assert.equal(feed.net, 6000);
  assert.equal(feed.gstEstimate, 680);
  assert.equal(feed.gstPaidOnPurchases, 600);
  assert.deepEqual(
    JSON.parse(JSON.stringify(feed.gstEstimateByCat)),
    {
      'Retail Sale': { income: 10000, ratePct: 5, estimate: 500 },
      'Service Income': { income: 1000, ratePct: 18, estimate: 180 }
    }
  );
});

test('ENG-01 leave golden case counts only approved weighted days inside the FY', () => {
  const context = evaluateModule('leave', {
    storage: {
      leavedesk_v3: {
        employees: [],
        agendas: {},
        leaves: {
          '2026-03-31': [{ name: 'Alice', category: 'Casual Leave', type: 'full_day', status: 'approved' }],
          '2026-04-01': [{ name: 'Alice', category: 'Casual Leave', type: 'full_day', status: 'approved' }],
          '2026-04-02': [{ name: 'Alice', category: 'Casual Leave', type: 'half_day', status: 'approved' }],
          '2026-04-03': [{ name: 'Alice', category: 'Casual Leave', type: 'full_day', status: 'pending' }],
          '2026-04-04': [{ name: 'Alice', category: 'Casual Leave', type: 'full_day', status: 'rejected' }]
        }
      }
    }
  });
  const usage = vm.runInContext(
    `fyUsage('alice', { from: '2026-04-01', to: '2027-03-31' })`,
    context
  );
  assert.deepEqual(JSON.parse(JSON.stringify(usage)), { 'Casual Leave': 1.5 });
});

test('ENG-01 requested leave excludes configured weekly offs', () => {
  const context = evaluateModule('leave', {
    elements: {
      leaveFrom: { value: '2026-07-20' },
      leaveTo: { value: '2026-07-26' },
      leaveType: { value: 'half_day' }
    }
  });
  assert.equal(vm.runInContext('requestedLeaveDays()', context), 2.5);
});

test('ENG-01 report golden case preserves exact financial rows and total', () => {
  const source = fs.readFileSync(path.join(rootDir, 'www/saagar-report.js'), 'utf8');
  const context = browserContext();
  vm.runInContext(source, context, { filename: 'saagar-report.js' });
  const artifact = context.SaagarReport._blocksToCsv({
    blocks: [{
      t: 'table',
      head: ['Description', 'Amount'],
      body: [['Cash income', 1300], ['Expenses', -250]],
      foot: ['Net', 1050]
    }]
  });
  assert.equal(
    artifact.text,
    '\ufeff"Description","Amount"\r\n"Cash income","1300"\r\n"Expenses","\'-250"\r\n"Net","1050"'
  );
  assert.equal(artifact.rowCount, 2);
});
