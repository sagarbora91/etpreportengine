import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';
import { LEGACY_WEBVIEW_PRELUDE, transformJavaScriptAsset } from '../scripts/prepare-api23-assets.mjs';

const pkg = JSON.parse(fs.readFileSync(new URL('../package.json', import.meta.url), 'utf8'));
const pipeline = fs.readFileSync(new URL('../scripts/prepare-api23-assets.mjs', import.meta.url), 'utf8');
const seeded = fs.readFileSync(new URL('../scripts/build-seeded-apk.mjs', import.meta.url), 'utf8');
const emulator = fs.readFileSync(new URL('../scripts/android-emulator.ps1', import.meta.url), 'utf8');
const productionRecipe = fs.readFileSync(new URL('../scripts/build-production-release.ps1', import.meta.url), 'utf8');
const shellCss = fs.readFileSync(new URL('../www/shell-core.css', import.meta.url), 'utf8');
const etpModule = fs.readFileSync(new URL('../www/modules/etp/index.html', import.meta.url), 'utf8');

test('normal and release APK builds prepare API-23 assets before Gradle', () => {
  assert.match(pkg.scripts['build:apk'], /prepare:api23.*gradlew\.bat assembleDebug/);
  assert.match(pkg.scripts['build:release'], /build-production-release\.ps1/);
  assert.match(productionRecipe, /prepare:api23/);
  assert.match(productionRecipe, /'clean', 'assembleRelease'/);
});

test('repository pins a reproducible API-23 emulator toolchain and npm workflow', () => {
  assert.match(pkg.scripts['android:configure'], /android-emulator\.ps1 configure/);
  assert.match(pkg.scripts['android:preflight'], /android-emulator\.ps1 preflight/);
  assert.match(pkg.scripts['emulator:api23:start'], /android-emulator\.ps1 start/);
  assert.match(emulator, /function Get-AvdSerials/);
  assert.match(emulator, /& \$adb -s \$serial emu kill/);
  assert.doesNotMatch(emulator, /& \$adb wait-for-device/);
  assert.match(emulator, /\.android-build/);
  assert.match(emulator, /system-images\\android-23\\default\\x86_64/);
  assert.match(emulator, /sdk\.dir=/);
  assert.match(emulator, /sys\.boot_completed/);
});

test('seeded APK is transformed after its generated-only seed mutation', () => {
  assert.match(seeded, /generatedSeeded[\s\S]*prepare-api23-assets\.mjs[\s\S]*assembleDebug/);
});

test('pipeline covers scripts, legacy CSS, bridge, runtime shims and generated pins', () => {
  for (const marker of ['@babel/preset-env', "chrome: '44'", 'transformHtml', 'resolveCssVariables', 'collectCssVariables', "ext === '.css'", 'native-bridge.js', 'Object.assign', 'NodeList.prototype.forEach', 'manifest.modules', 'manifest.sharedAssets', "createHash('sha256')"]) {
    assert.ok(pipeline.includes(marker), `missing ${marker}`);
  }
});

test('API-23 assets force deterministic flex and iframe layout fallbacks', () => {
  assert.match(pipeline, /legacyClass='saagar-legacy-webview'/);
  assert.match(pipeline, /parseInt\(chromeMatch\[1\],10\)<57/);
  assert.match(shellCss, /\.module-frame\{position:absolute;top:0;right:0;bottom:0;left:0/);
  assert.match(shellCss, /html\.saagar-legacy-webview \.module-screen\{display:-webkit-flex;display:flex/);
  assert.match(shellCss, /html\.saagar-legacy-webview \.frame-wrap\{-webkit-flex:1 1 auto;flex:1 1 auto;min-height:0\}/);
  assert.match(etpModule, /html\.saagar-legacy-webview \.form-grid/);
  assert.match(etpModule, /html\.saagar-legacy-webview \.file-grid/);
  assert.match(etpModule, /html\.saagar-legacy-webview \.file-field input\{top:0;right:0;bottom:0;left:0\}/);
});

function compatibilityContext(userAgent) {
  function Element() { this.children = []; }
  Object.defineProperty(Element.prototype, 'firstChild', { get() { return this.children[0] || null; } });
  Element.prototype.removeChild = function (child) { this.children.splice(this.children.indexOf(child), 1); };
  Element.prototype.appendChild = function (child) { this.children.push(child); return child; };
  const document = {
    documentElement: { className: '' },
    createTextNode(value) { return { nodeType: 3, textContent: value }; }
  };
  const window = { Element, NodeList: function () {}, HTMLCollection: function () {} };
  return { window, document, navigator: { userAgent }, Element, NodeList: window.NodeList, HTMLCollection: window.HTMLCollection };
}

test('Chrome-44 compatibility prelude supplies replaceChildren and limits fallback class to legacy WebViews', () => {
  const legacy = compatibilityContext('Mozilla/5.0 Chrome/44.0.2403.119 Mobile Safari/537.36');
  vm.runInNewContext(LEGACY_WEBVIEW_PRELUDE, legacy);
  assert.match(legacy.document.documentElement.className, /saagar-legacy-webview/);
  assert.equal(typeof legacy.Element.prototype.replaceChildren, 'function');
  const host = new legacy.Element();
  host.appendChild({ nodeType: 1, name: 'old' });
  host.replaceChildren('No verified scope has been published.');
  assert.equal(host.children.length, 1);
  assert.equal(host.children[0].textContent, 'No verified scope has been published.');

  const modern = compatibilityContext('Mozilla/5.0 Chrome/120.0.0.0 Mobile Safari/537.36');
  vm.runInNewContext(LEGACY_WEBVIEW_PRELUDE, modern);
  assert.doesNotMatch(modern.document.documentElement.className, /saagar-legacy-webview/);
  assert.equal(typeof modern.Element.prototype.replaceChildren, 'function');
});

test('API-23 preparation preserves only the exact canonical build identity bytes', () => {
  const identity = fs.readFileSync(new URL('../www/build-identity.js', import.meta.url), 'utf8');
  const modern = 'const build = () => ({ value: 1 });';

  assert.equal(
    transformJavaScriptAsset(identity, 'build-identity.js', 'build-identity.js'),
    identity,
    'canonical identity bytes must not be rewritten'
  );
  assert.equal(
    transformJavaScriptAsset(identity, 'build-identity.js', 'build-identity.js'),
    transformJavaScriptAsset(identity, 'build-identity.js', 'build-identity.js'),
    'identity preservation must be deterministic'
  );

  const transformed = transformJavaScriptAsset(modern, 'feature.js', 'feature.js');
  assert.notEqual(transformed, modern);
  assert.doesNotMatch(transformed, /=>/);
  assert.doesNotMatch(transformed, /\bconst\b/);

  const nested = transformJavaScriptAsset(modern, 'nested/build-identity.js', 'nested/build-identity.js');
  assert.notEqual(nested, modern, 'the authority exception must not match a nested filename');
});

test('API-23 storage uses native-first migration and never invokes wasm when unsupported', () => {
  const storage = fs.readFileSync(new URL('../www/storage-core.js', import.meta.url), 'utf8');
  assert.match(storage, /typeof WebAssembly !== 'object'[\s\S]*migrateToNative\(plugin\)/);
  assert.match(storage, /WebAssembly absent - native-LS fallback/);
  assert.match(storage, /native-first migration active without WebAssembly/);
  assert.match(shellCss, /@supports not \(display:grid\)/);
  assert.match(shellCss, /\.app\{display:-webkit-flex;display:flex/);
});
