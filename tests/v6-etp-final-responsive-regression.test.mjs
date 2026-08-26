import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

const read = relative => fs.readFileSync(new URL(relative, import.meta.url), 'utf8');
const moduleHtml = read('../www/modules/etp/index.html');
const api23Pipeline = read('../scripts/prepare-api23-assets.mjs');
const surfaces = [
  ['e3', read('../www/etp-e3-presentation.css')],
  ['e4', read('../www/etp-e4-presentation.css')],
  ['e5', read('../www/etp-e5-presentation.css')],
  ['e6', read('../www/etp-e6-presentation.css')],
  ['e7', read('../www/etp-e7-presentation.css')]
];

test('mounted E3-E7 and language surfaces load their responsive styles exactly once', () => {
  for (const name of ['e3', 'e4', 'e5', 'e6', 'e7']) {
    const matches = moduleHtml.match(new RegExp(`href="\\.\\.\\/\\.\\.\\/etp-${name}-presentation\\.css"`, 'g')) || [];
    assert.equal(matches.length, 1, `${name} presentation CSS must be mounted once`);
  }
  assert.equal((moduleHtml.match(/href="\.\.\/\.\.\/etp-operational-i18n\.css"/g) || []).length, 1);
});

test('all operational presentations contain long text without horizontal page sliding', () => {
  for (const [name, css] of surfaces) {
    assert.match(css, /max-width:100%/, `${name} max-width containment`);
    assert.match(css, /(?:overflow-wrap:anywhere|word-break:break-word)/, `${name} long-text wrapping`);
    assert.match(css, /min-height:44px/, `${name} touch target`);
    assert.doesNotMatch(css, /100vw|overflow-x:\s*(?:auto|scroll)/, `${name} page-level horizontal sliding`);
  }
});

test('phone stacking does not remove the intentional desktop grids', () => {
  assert.match(surfaces[0][1], /@media\(min-width:700px\)[\s\S]*repeat\(2,minmax\(0,1fr\)\)/);
  assert.match(surfaces[0][1], /@media\(min-width:1000px\)[\s\S]*repeat\(4,minmax\(0,1fr\)\)/);
  assert.match(surfaces[1][1], /repeat\(auto-fit,minmax\(min\(100%,180px\),1fr\)\)/);
  for (const [name, css] of surfaces.slice(2)) {
    assert.match(css, /@media\(min-width:900px\)/, `${name} desktop breakpoint`);
    assert.match(css, /repeat\([34],minmax\(0,1fr\)\)/, `${name} desktop grid`);
  }
});

test('API23 legacy-WebView marker has an explicit no-grid fallback for every new surface', () => {
  assert.match(api23Pipeline, /legacyClass='saagar-legacy-webview'/);
  assert.match(api23Pipeline, /parseInt\(chromeMatch\[1\],10\)<57/);
  for (const [name, css] of surfaces) {
    assert.match(css, /\.saagar-legacy-webview/, `${name} legacy marker`);
    assert.match(css, /display:block/, `${name} no-grid fallback`);
  }
  const i18n = read('../www/etp-operational-i18n.css');
  assert.match(i18n, /\.saagar-legacy-webview/);
  assert.match(i18n, /display:block/);
});

test('BLOCKED and READY presentation state containers remain bounded under translated text', () => {
  const i18n = read('../www/etp-operational-i18n.css');
  assert.match(i18n, /\[data-etp-i18n-surface\][^{]*\{[^}]*min-width:0[^}]*max-width:100%/);
  assert.match(i18n, /button\{min-height:44px\}/);
  assert.doesNotMatch(i18n, /100vw|overflow-x:\s*(?:auto|scroll)/);
  for (const [name, css] of surfaces.slice(1)) {
    assert.match(css, new RegExp(`\\.etp-${name}-state--blocked`));
    assert.match(css, new RegExp(`\\.etp-${name}-state--ready`));
  }
});
