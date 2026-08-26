import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import vm from 'node:vm';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { readModuleManifestSource } from '../scripts/lib/module-manifest-source.mjs';

const require = createRequire(import.meta.url);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const manifestPath = path.join(root, 'www', 'module-manifest.js');
const shellPath = path.join(root, 'www', 'index.html');
const source = fs.readFileSync(manifestPath, 'utf8');
const shell = fs.readFileSync(shellPath, 'utf8');
const api = require(manifestPath);
const expectedIds = [
  'stock', 'service', 'qms', 'dsr', 'expense', 'grooming',
  'cro_audit', 'payroll', 'leave', 'tax', 'planning', 'etp'
];

function rawClone() {
  return JSON.parse(JSON.stringify({ schemaVersion: api.schemaVersion, sharedAssets: api.sharedAssets, modules: api.modules }));
}

test('MAH-2 manifest is the synchronous authority before the compatibility alias', () => {
  const tag = '<script src="module-manifest.js"></script>';
  assert.equal(shell.split(tag).length - 1, 1);
  const tagAt = shell.indexOf(tag);
  const aliasAt = shell.indexOf('const MODULES =');
  assert.ok(tagAt >= 0 && aliasAt > tagAt);
  assert.doesNotMatch(shell, /const\s+MODULES\s*=\s*\[/);
  assert.doesNotMatch(tag, /\b(?:async|defer)\b/);
});

test('MAH-2 manifest has exact ordered modules and immutable browser data', () => {
  assert.equal(api.schemaVersion, 2);
  assert.deepEqual(api.ids, expectedIds);
  assert.deepEqual(api.modules.map(module => module.id), expectedIds);
  assert.equal(Object.isFrozen(api), true);
  assert.equal(Object.isFrozen(api.ids), true);
  assert.equal(Object.isFrozen(api.modules), true);
  assert.equal(Object.isFrozen(api.sharedAssets), true);
  assert.equal(Object.isFrozen(api.sharedAssets[0]), true);
  assert.equal(api.getShared('module-runtime'), api.sharedAssets[1]);
  assert.equal(api.getShared('unknown'), null);
  api.modules.forEach(module => assert.equal(Object.isFrozen(module), true, module.id));
  assert.equal(api.get('stock'), api.modules[0]);
  assert.equal(api.get('unknown'), null);
  assert.equal(api.has('planning'), true);
  assert.equal(api.has('etp'), true);
  assert.equal(api.has('unknown'), false);
});

test('manifest binds both synchronous shared runtimes to local bytes and SHA-256', () => {
  assert.equal(api.sharedAssets.length, 66);
  const bridge = api.sharedAssets[0];
  assert.equal(bridge.id, 'module-bridge');
  assert.equal(bridge.file, 'shared/module-bridge.js');
  const runtime = api.sharedAssets[1];
  assert.deepEqual(Object.keys(runtime), ['id', 'version', 'file', 'bytes', 'sha256']);
  assert.equal(runtime.id, 'module-runtime');
  assert.equal(runtime.version, 1);
  assert.equal(runtime.file, 'shared/module-runtime.js');
  const mah4Runtime = api.sharedAssets[2];
  assert.equal(mah4Runtime.id, 'mah4-runtime');
  assert.equal(mah4Runtime.version, 1);
  assert.equal(mah4Runtime.file, 'shared/mah4-runtime.js');
  assert.deepEqual(api.sharedAssets.slice(3).map(asset => asset.id), ['module-uniform-css','module-back-css','module-employee-css','module-mobile-common-css','module-brand-tokens-css','module-responsive-css','module-ui-runtime','module-table-css','module-table-runtime','module-components-css','module-rendered-components','stock-ui-css','payroll-ui-css','grooming-ui-css','service-ui-css','leave-ui-css','cro-audit-ui-css','tax-ui-css','dsr-ui-css','qms-view','qms-ui-css','module-delete-cell-css','etp-verified-presentation','etp-verified-analytics','etp-analytics-consumer','etp-operational-foundation','etp-operational-store','etp-operational-adapters','etp-operational-runtime','etp-e4-authority-intake','etp-e6-authority-intake','etp-e5-authority-intake','etp-e7-authority-intake','etp-e7-service-verifier','etp-e7-service-operational','etp-cro-reconciliation','etp-e3-orchestrator','etp-e3-presentation','etp-e3-presentation-css','etp-target-planning','etp-e4-orchestrator','etp-e4-presentation','etp-e4-presentation-css','etp-e6-presentation','etp-e6-presentation-css','etp-e5-presentation','etp-e5-presentation-css','etp-operational-i18n','etp-operational-i18n-css','etp-e5-payroll-bridge','etp-e7-presentation','etp-e7-presentation-css','etp-e7-module-host','etp-operational-gateway','etp-operational-mount','etp-e3-verified-join','etp-operational-bootstrap','etp-operational-shell-composer','etp-operational-module-host','etp-operational-frame-bridge','etp-exception-monitor','etp-incentive-control','etp-operations-consumer']);
  assert.equal(api.getShared('mah4-runtime'), mah4Runtime);
  for (const asset of api.sharedAssets) {
    const bytes = fs.readFileSync(path.join(root, 'www', asset.file));
    assert.equal(asset.bytes, bytes.length, asset.id);
    assert.equal(asset.sha256, crypto.createHash('sha256').update(bytes).digest('hex'), asset.id);
  }
});

test('module bridge is a versioned local boundary for shell-owned capabilities', () => {
  const bridge = fs.readFileSync(path.join(root, 'www/shared/module-bridge.js'), 'utf8');
  assert.match(bridge, /var MODULE_BRIDGE_VERSION = 1;/);
  for (const name of ['adminPinCheck', 'ownerSession', 'sharedStorage', 'evidence', 'legal', 'reauth', 'report', 'qmsPolicy', 'photo', 'ensureJsZip', 'e7ServiceVerification', 'e7ServiceActor', 'getE7ServiceActionInput']) {
    assert.match(bridge, new RegExp(`\\b${name}: getter`), name);
  }
  for (const module of api.modules) {
    const html = fs.readFileSync(path.join(root, 'www', module.src), 'utf8');
    const bridgeAt = html.indexOf('../../shared/module-bridge.js');
    const runtimeAt = html.indexOf('../../shared/module-runtime.js');
    assert.ok(bridgeAt >= 0 && bridgeAt < runtimeAt, `${module.id} bridge load order`);
  }
});

test('MAH-2 manifest binds every exact local path to its raw bytes and SHA-256', () => {
  for (const module of api.modules) {
    assert.equal(module.file, `modules/${module.id}/index.html`, module.id);
    assert.equal(module.src, module.file, module.id);
    assert.equal(module.html_b64, undefined, module.id);
    assert.doesNotMatch(module.src, /^(?:[a-z]+:|\/\/)|[\\?#%]|(?:^|\/)\.\.?\//i, module.id);
    const bytes = fs.readFileSync(path.join(root, 'www', module.src));
    assert.equal(module.bytes, bytes.length, module.id);
    assert.equal(module.sha256, crypto.createHash('sha256').update(bytes).digest('hex'), module.id);
  }
});

test('MAH-2 manifest rejects unknown, missing, duplicate, remote and malformed records', () => {
  const cases = [
    value => { value.extra = true; },
    value => { value.schemaVersion = 1; },
    value => { delete value.sharedAssets; },
    value => { value.sharedAssets[0].file = 'https://example.invalid/runtime.js'; },
    value => { value.sharedAssets[0].sha256 = 'A'.repeat(64); },
    value => { delete value.modules[0].summary; },
    value => { value.modules[0].unexpected = true; },
    value => { value.modules[0].id = 'service'; },
    value => { value.modules[0].src = 'https://example.invalid/stock.html'; },
    value => { value.modules[0].src = '../stock.html'; },
    value => { value.modules[0].file = 'modules/service/index.html'; },
    value => { value.modules[0].bytes = 0; },
    value => { value.modules[0].sha256 = 'A'.repeat(64); },
    value => { value.modules[1].sha256 = value.modules[0].sha256; },
    value => { value.modules[0].title = ` ${value.modules[0].title}`; }
  ];
  for (const mutate of cases) {
    const candidate = rawClone();
    mutate(candidate);
    assert.throws(() => api.validate(candidate), /Invalid Saagar module manifest/);
  }
});

test('MAH-2 manifest has one machine-editable data block and Node/browser parity', () => {
  const snapshot = readModuleManifestSource(root);
  assert.deepEqual(snapshot.data, rawClone());
  const context = { window: {}, console: { error() {} } };
  vm.runInNewContext(source, context, { filename: 'module-manifest.js' });
  const browserApi = context.window.SaagarModuleManifest;
  assert.ok(browserApi);
  assert.equal(JSON.stringify(browserApi.modules), JSON.stringify(api.modules));
  assert.equal(JSON.stringify(browserApi.sharedAssets), JSON.stringify(api.sharedAssets));
  assert.equal(Object.isFrozen(browserApi.modules), true);
  assert.equal(Object.isFrozen(browserApi.modules[0]), true);
});

test('MAH-2 shell keeps a recoverable fail-closed missing-manifest guard', () => {
  assert.match(shell, /window\.SaagarModuleManifest/);
  assert.match(shell, /Module manifest unavailable or invalid/);
  assert.match(shell, /if\(!Array\.isArray\(MODULES\) \|\| !MODULES\.length\)/);
  assert.match(shell, /Module manifest integrity/);
  assert.match(shell, /External module routes/);
  assert.doesNotMatch(shell, /Module decode integrity|Embedded module count|__warmIds/);
});
