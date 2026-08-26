import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const modulePath = path.join(root, 'www/modules/etp/index.html');
const etp = fs.readFileSync(modulePath, 'utf8');
const shell = fs.readFileSync(path.join(root, 'www/index.html'), 'utf8');
const manifest = require('../www/module-manifest.js');

test('ETP is one manifest-bound Reports module with the standard MAH-4 lifecycle', () => {
  const entry = manifest.get('etp');
  assert.ok(entry);
  assert.equal(entry.category, 'Reports');
  assert.equal(entry.src, 'modules/etp/index.html');
  for (const asset of ['module-bridge.js', 'module-runtime.js', 'mah4-runtime.js']) {
    assert.equal((etp.match(new RegExp(`shared/${asset.replace('.', '\\.')}`, 'g')) || []).length, 1, asset);
  }
  assert.match(etp, /SaagarModuleRuntime\.run\('storage',\{schemaVersion:1,moduleId:'etp'/);
  assert.match(etp, /SaagarModuleRuntime\.run\('safety',\{schemaVersion:1,moduleId:'etp'/);
  assert.match(etp, /SaagarMah4Runtime\.bootModule\(\{moduleId:'etp'\}\)/);
});

test('ETP shell exposes the four bounded Reports-owned surfaces', () => {
  for (const label of ['Import &amp; Validate', 'Verified Reports', 'Coverage &amp; History', 'Reconciliation &amp; Exceptions']) {
    assert.match(etp, new RegExp(label));
  }
  for (const report of ['R003', 'R013', 'R022', 'R025', 'PAYMENTTYPE25']) assert.match(etp, new RegExp(report));
});

test('ETP import confirmation controls preserve measured accessibility thresholds', () => {
  assert.match(etp, /\.coverage-check input\{[^}]*width:44px;height:44px/);
  assert.match(etp, /\.action\.primary\{[^}]*background:#7a5a00;color:#fff/);
  assert.match(etp, /\.action\.primary:disabled\{[^}]*background:#667085;color:#fff;opacity:1/);
});

test('ETP module uses only the future narrow bridge gateway and has no direct fact/store capability', () => {
  const bridge = fs.readFileSync(path.join(root, 'www/shared/module-bridge.js'), 'utf8');
  assert.match(bridge, /etpImportGateway: getter\(function \(\) \{ var gateway = parentValue\('SaagarEtpModuleGateway'\); return gateway && gateway\.importFacade; \}\)/);
  assert.match(bridge, /etpReadGateway: getter\(function \(\) \{ var gateway = parentValue\('SaagarEtpModuleGateway'\); return gateway && gateway\.readFacade; \}\)/);
  assert.match(etp, /SaagarModuleBridge/);
  assert.match(etp, /bridge\.etpReadGateway\.listScopes/);
  assert.match(etp, /bridge\.etpImportGateway\.run/);
  assert.doesNotMatch(etp, /SaagarEtp(?:NativeStore|VerifiedReader|ImportRuntime)|readFacts\s*\(|Capacitor\.Plugins|localStorage|indexedDB|window\.parent|parent\.postMessage/);
});

test('shell loads versioned readiness and query contracts before the parent gateway', () => {
  const foundation = shell.indexOf('<script src="etp-foundation-status.js"></script>');
  const query = shell.indexOf('<script src="etp-query-contract.js"></script>');
  const gateway = shell.indexOf('<script src="etp-module-gateway.js"></script>');
  assert.ok(foundation >= 0 && query > foundation && gateway > query);
});

test('Reports routes through governed module navigation and Settings remains ETP-free', () => {
  const reports = shell.slice(shell.indexOf('id="reportsView"'), shell.indexOf('id="configView"'));
  const settingsStart = shell.indexOf('id="configView"');
  const settings = shell.slice(settingsStart, shell.indexOf('</section>', settingsStart) + 10);
  assert.match(reports, /navigateToModule\('etp'\)/);
  assert.doesNotMatch(reports, /SaagarEtpImportUi\.open/);
  assert.doesNotMatch(settings, /Retail ETP|Open Retail ETP|navigateToModule\('etp'\)/);
});

test('ETP participates in least-privilege access, PIN and module grouping registries', () => {
  assert.match(shell, /const ACCESS_MODULES = \[[^\]]*"etp"\]/);
  assert.match(shell, /\['Reports & planning',\s*\['etp','planning'\]\]/);
  assert.match(shell, /"Cashier":ONLY\("expense"\)/);
  assert.match(shell, /"CRO":ONLY\("qms","dsr","grooming","cro_audit"\)/);
  assert.match(shell, /"Others":ONLY\(\)/);
  const pin = require('../www/module-pin-policy.js');
  assert.equal(pin.isKnownModule('etp'), true);
  assert.equal(pin.defaults().modules.etp, false);
});
