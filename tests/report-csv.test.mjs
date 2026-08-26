import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const rootDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const source = fs.readFileSync(path.join(rootDir, 'www/saagar-report.js'), 'utf8');

function loadReportEngine() {
  const localStorage = {
    getItem: () => null,
    setItem: () => {},
    removeItem: () => {}
  };
  const window = {};
  window.window = window;
  vm.runInNewContext(source, {
    window,
    globalThis: window,
    localStorage,
    Blob,
    Date,
    Math,
    JSON,
    Number,
    String,
    Array,
    Object,
    Promise,
    setTimeout,
    clearTimeout
  }, { filename: 'saagar-report.js' });
  return window.SaagarReport;
}

test('P1-46 CSV companion preserves table rows, totals and spreadsheet safety', () => {
  const report = loadReportEngine();
  const artifact = report._blocksToCsv({
    blocks: [{
      t: 'table',
      head: ['Name', 'Amount'],
      body: [
        ['=HYPERLINK("https://invalid.example")', 10],
        ['Normal, quoted', 20]
      ],
      foot: ['TOTAL', 30]
    }]
  });

  assert.equal(artifact.rowCount, 2);
  assert.equal(
    artifact.text,
    '\ufeff"Name","Amount"\r\n"\'=HYPERLINK(""https://invalid.example"")","10"\r\n"Normal, quoted","20"\r\n"TOTAL","30"'
  );
});

test('P1-46 exposes CSV beside PDF for the four register reports', () => {
  for (const type of ['payrollRegister', 'expenseMonthly', 'leaveRegister', 'stockRegister']) {
    assert.match(source, new RegExp(`${type}: true`));
  }
  assert.match(source, /savePreviewCsv/);
  assert.match(source, /report-preview-csv/);
});
