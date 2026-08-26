import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { transformHtml } from '../scripts/prepare-api23-assets.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoDir = path.resolve(here, '..');
const index = fs.readFileSync(path.join(repoDir, 'www', 'index.html'), 'utf8');
const seed = fs.readFileSync(path.join(repoDir, 'www', 'demo-seed.js'), 'utf8');
const build = fs.readFileSync(
  path.join(repoDir, 'scripts', 'build-seeded-apk.mjs'),
  'utf8'
);

test('production-oriented source remains clean while the seeded builder targets generated assets only', () => {
  assert.match(index, /\bvar\s+DEMO_SEED_ENABLED\s*=\s*false\s*;/);
  assert.doesNotMatch(index, /\bvar\s+DEMO_SEED_ENABLED\s*=\s*true\s*;/);
  assert.match(build, /generatedIndexPath/);
  assert.match(build, /sourceManifestPath/);
  assert.match(build, /generatedManifestPath/);
  assert.ok(build.includes('sourceManifest.equals(generatedManifest)'));
  assert.ok(build.includes('shellAt <= manifestAt'));
  assert.match(build, /var DEMO_SEED_ENABLED = true;/);
  assert.match(build, /daysBack:\s*730/);
  assert.match(build, /walkInsPerWorkingDay:\s*25/);
  assert.match(build, /finally\s*\{[\s\S]*generatedClean/);
  assert.doesNotMatch(build, /writeFileSync\(sourceIndexPath/);

  const variables = new Map([
    ['font-serif', "'DM Serif Display',Georgia,serif"],
    ['navy', '#0d2340']
  ]);
  const transformed = transformHtml(
    '<html><head><style>.title{color:var(--navy)}</style></head><body>' +
      '<div style="color:var(--navy)">Safe</div>' +
      '<script>var card=\'<div style="font-family:var(--font-serif)">Value</div>\';</script>' +
      '</body></html>',
    'api23-css-string-fixture.html',
    variables
  );
  assert.match(transformed, /\.title\{color:#0d2340\}/);
  assert.match(transformed, /<div style="color:#0d2340">Safe<\/div>/);
  const inline = [...transformed.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)]
    .map(match => match[1])
    .find(body => body.includes('font-family:var(--font-serif)'));
  assert.ok(inline);
  assert.match(inline, /font-family:var\(--font-serif\)/);
  assert.doesNotThrow(() => new vm.Script(inline));
});

test('normal SQLite-primary runtime keeps production build flavor', () => {
  const buildFlavorSource = index.match(/function buildFlavor\(\)\{[\s\S]*?\n\}/)?.[0];
  assert.ok(buildFlavorSource, 'buildFlavor function must remain present');
  assert.match(buildFlavorSource, /__DEMO_SEED_ACTIVE\s*===\s*true/);
  assert.match(buildFlavorSource, /__FORCE_STORAGE_CORE\s*===\s*true/);
  assert.doesNotMatch(buildFlavorSource, /SaagarStore/,
    'the shipped SQLite-primary runtime is not a TEST build signal');
  assert.match(buildFlavorSource, /return\s+'PROD'/);
});

test('demo shell exposes an unmistakable synthetic-data profile and banner only when active', () => {
  assert.match(index, /__SAAGAR_DEMO_PROFILE/);
  assert.match(index, /SYNTHETIC DEMO DATA/);
  assert.match(index, /__DEMO_SEED_ACTIVE/);
  assert.match(index, /#0d2340/);
  assert.match(index, /#b8922a/);
  assert.match(index, /bottom:76px;pointer-events:none/);
  assert.match(index, /data-no-i18n/);
});

test('long-history seeding is deterministic, chunked and metadata-labelled', () => {
  assert.match(seed, /async function __runSeedBig/);
  assert.match(seed, /bulkAsync/);
  assert.match(seed, /KEEP_DAYS = 90/);
  assert.match(seed, /saagar_demo_profile_v1/);
  assert.match(seed, /syntheticOnly:\s*true/);
  assert.match(seed, /v4_' \+ DAYS \+ 'd_' \+ WALK \+ 'w/);
  assert.match(seed, /Preparing ' \+ \(DAYS \+ 1\) \+ ' days of synthetic demo data/);
});

test('seeded contact numbers are structurally valid but use a non-routable demo prefix', () => {
  assert.doesNotMatch(seed, /mobile:\s*'9[78]'/);
  assert.doesNotMatch(seed, /phone:\s*'90'/);
  assert.match(seed, /function demoMobile/);
  const source = seed.match(/function demoMobile\(kind, n\) \{[^}]+\}/)?.[0];
  assert.ok(source);
  const context = {};
  vm.createContext(context);
  vm.runInContext(`${source}; this.demoMobile = demoMobile;`, context);
  const values = [
    context.demoMobile('employee', 1),
    context.demoMobile('qms', 123),
    context.demoMobile('service', 456)
  ];
  values.forEach(value => assert.match(value, /^1\d{9}$/));
  assert.equal(new Set(values).size, values.length);
});

test('two-year profile seeds D3 Service workflow variety and payroll archive snapshots', () => {
  assert.match(seed, /var OPEN_STAGES = \['received', 'awaiting_approval', 'in_progress', 'ready', 'on_hold'\]/);
  assert.match(seed, /d3Readiness/);
  assert.match(seed, /d3Transitions/);
  assert.match(seed, /REPEAT-DEMO/);
  assert.match(seed, /for \(var pm = 1; pm <= 24; pm\+\+\)/);
  assert.match(seed, /status:\s*'locked'/);
  assert.match(seed, /snapshot:\s*snapshot/);
});
