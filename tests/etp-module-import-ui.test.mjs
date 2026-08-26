import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { inlineModuleScripts } from './lib/module-bundle.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const html = fs.readFileSync(path.join(root, 'www/modules/etp/index.html'), 'utf8');

test('ETP module exposes an FY dropdown and four bounded multi-file report controls', () => {
  inlineModuleScripts(html).forEach((source, index) => assert.doesNotThrow(() => new vm.Script(source, { filename: `etp-ui-${index}.js` })));
  for (const field of ['storeCode', 'financialYear', 'periodStart', 'periodEnd']) {
    assert.match(html, new RegExp(`data-etp-scope="${field}"`));
  }
  for (const report of ['R003', 'R013', 'R022', 'R025']) {
    assert.match(html, new RegExp(`data-etp-file="${report}"`));
  }
  assert.equal((html.match(/type="file"/g) || []).length, 4);
  assert.equal((html.match(/type="file" multiple/g) || []).length, 4);
  assert.match(html, /id="etpFinancialYear"/);
  assert.match(html, /function populateFinancialYears\(\)/);
  assert.match(html, /accept="\.xlsx,application\/vnd\.openxmlformats-officedocument\.spreadsheetml\.sheet"/);
});

test('validation sends only scope and files while trusted gateway owns coverage authority', () => {
  assert.match(html, /api\.run\(\{\s*scope: selectedScope,\s*files: selectedFiles\(\)/);
  assert.match(html, /result\.push\(\{ selectedReportId: id, file: file \}\)/);
  assert.match(html, /coverageConfirmed: true/);
  assert.doesNotMatch(html, /confirmedByRole|coverageDeclaration: declaration/);
  assert.match(html, /if \(!filesReady\(\)\)/);
  assert.match(html, /if \(!document\.getElementById\('etpCoverageConfirmed'\)\.checked\)/);
});

test('terminal success releases selected workbooks and coverage state', () => {
  assert.match(html, /function resetImportFiles\(\)/);
  assert.match(html, /state\.files\[id\] = \[\]/);
  assert.match(html, /input\.value = ''/);
  assert.match(html, /etpCoverageConfirmed'\)\.checked = false/);
  assert.ok((html.match(/resetImportFiles\(\);/g) || []).length >= 2);
  assert.match(html, /Selected workbook bytes are used only for validation and are released after a terminal success/);
});

test('scope validation checks a real consecutive financial year and period membership', () => {
  assert.match(html, /Number\(match\[2\]\) !== \(Number\(match\[1\]\) \+ 1\) % 100/);
  assert.match(html, /function fy\(date\)/);
  assert.match(html, /toISOString\(\)\.slice\(0, 10\) !== value\.periodStart/);
  assert.match(html, /fy\(start\) === value\.financialYear && fy\(end\) === value\.financialYear/);
});

test('monthly source exports are combined but bounded to the selected publication scope', () => {
  assert.match(html, /Monthly exports are combined locally/);
  assert.match(html, /only rows inside the explicitly selected one-year scope are reconciled and published/);
  assert.match(html, /Rows outside this scope remain unpublished/);
});

test('publication consumes only the opaque confirmation token and defaults fail closed', () => {
  assert.match(html, /\^confirm_\[a-f0-9\]\{32\}_\\d\+\$/);
  assert.match(html, /api\.confirm\(\{ confirmationToken: token \}\)/);
  assert.match(html, /state\.confirmationToken = '';/);
  assert.match(html, /Nothing was published/);
  assert.match(html, /Publication stopped safely/);
  assert.doesNotMatch(html, /lifecycle\s*:/);
});

test('coverage/history is bounded, metadata-only and rendered without HTML injection', () => {
  assert.match(html, /api\.listScopes\(\{ limit: 20 \}\)/);
  assert.match(html, /api\.inspectScope\(selectedScope, \{ historyLimit: 10 \}\)/);
  assert.match(html, /currentReceipt\.coverage/);
  assert.match(html, /result\.history/);
  assert.match(html, /function renderScopeError\(code\)/);
  assert.doesNotMatch(html, /text\(document\.getElementById\('etpScopeList'\), 'Scope unavailable/);
  assert.doesNotMatch(html, /\.innerHTML\s*=|insertAdjacentHTML|document\.write/);
});

test('module has no direct native, fact, storage, export or parent capability', () => {
  assert.match(html, /SaagarModuleBridge/);
  assert.match(html, /bridge\.etpImportGateway/);
  assert.match(html, /bridge\.etpReadGateway/);
  assert.doesNotMatch(html, /bridge\.etpGateway/);
  assert.doesNotMatch(html, /SaagarEtp(?:NativeStore|VerifiedReader|ImportRuntime)|readFacts\s*\(|Capacitor\.Plugins|localStorage|indexedDB|window\.parent|parent\.postMessage|navigator\.share|\.download\s*=/);
});

test('ETP-3 integration hooks coexist with accessible tab and live-status behavior', () => {
  for (const hook of ['data-etp-verified-root', 'data-etp-view-tab="verified"', 'data-etp-view-tab="exceptions"', 'data-etp-verified-refresh', 'data-etp-verified-content', 'data-etp-exceptions-content']) {
    assert.match(html, new RegExp(hook));
  }
  assert.match(html, /role="status" aria-live="polite"/);
  assert.match(html, /role="tablist"/);
  assert.match(html, /event\.key !== 'ArrowRight' && event\.key !== 'ArrowLeft'/);
});

test('published scope selection survives tab changes and drives verified and exceptions refresh', () => {
  assert.match(html, /activeScope: null/);
  assert.match(html, /function setActiveScope\(value, refreshVisible\)/);
  assert.match(html, /setActiveScope\(item\.scope, true\); inspect\(item\.scope\)/);
  assert.match(html, /setActiveScope\(available\[0\], true\)/);
  assert.match(html, /getScope:function\(\)\{return state\.activeScope;\}/);
  assert.match(html, /view === 'verified' \|\| view === 'reconciliation'/);
  assert.equal((html.match(/data-etp-active-scope=/g) || []).length, 2);
  assert.equal((html.match(/data-etp-scope-picker/g) || []).length, 3);
  assert.equal((html.match(/data-etp-verified-refresh/g) || []).length, 2);
});

test('verified presentation has explicit responsive status and aggregation styles', () => {
  for (const cls of ['etp-active-scope', 'etp-v-metrics', 'etp-v-metric', 'etp-v-groups', 'etp-v-error', 'etp-v-quarantine']) assert.match(html, new RegExp('\\.' + cls));
});

test('legacy WebView fallback is isolated from modern tablets and covers generated list rows', () => {
  assert.match(html, /html\.saagar-legacy-webview \.form-grid/);
  assert.match(html, /html\.saagar-legacy-webview \.etp-v-groups>\*/);
  assert.match(html, /html\.saagar-legacy-webview \.file-field strong/);
  assert.doesNotMatch(html, /html\.saagar-api23/);
});
