import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadModuleBundle } from './lib/module-bundle.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const moduleCss = fs.readFileSync(path.join(root, 'www/mobile-layout.css'), 'utf8');
const shellCss = fs.readFileSync(path.join(root, 'www/mobile-shell.css'), 'utf8');

test('every external module loads the final phone remediation stylesheet', () => {
  for (const module of loadModuleBundle()) {
    assert.match(module.html, /<link rel="stylesheet" href="\.\.\/\.\.\/mobile-layout\.css">/, module.id);
  }
});

test('shell loads its phone-only responsive layer', () => {
  const shell = fs.readFileSync(path.join(root, 'www/index.html'), 'utf8');
  assert.match(shell, /<link rel="stylesheet" href="mobile-shell\.css">/);
  assert.match(shellCss, /@media \(max-width: 480px\)/);
});

test('module remediation is phone-scoped and coordinates fixed actions', () => {
  assert.match(moduleCss, /@media \(max-width: 640px\)/);
  assert.match(moduleCss, /#c1-desk-btn[\s\S]*right: 68px !important/);
  assert.match(moduleCss, /#st-v5-home-fab[\s\S]*right: 14px !important/);
  assert.match(moduleCss, /min-height: 44px !important/);
});

test('high-risk modules have explicit responsive containment', () => {
  for (const id of ['stock', 'expense', 'payroll', 'leave', 'tax', 'cro_audit']) {
    assert.match(moduleCss, new RegExp('data-mod="' + id + '"'), id);
  }
  assert.match(moduleCss, /grid-template-columns: repeat\(5, minmax\(0, 1fr\)\)/);
  assert.match(moduleCss, /data-mod="tax"[\s\S]*position: static !important/);
  assert.match(moduleCss, /data-mod="cro_audit"[\s\S]*\.submit-dk/);
});

test('phone tab rails stay contained and expose further content', () => {
  assert.match(moduleCss, /scroll-snap-type: x proximity/);
  assert.match(moduleCss, /-webkit-mask-image: linear-gradient/);
  assert.match(moduleCss, /overflow-x: auto !important/);
});

test('landscape mobile mode keeps the shared action dock separated', () => {
  assert.match(moduleCss, /@media \(min-width: 641px\) and \(max-width: 920px\)/);
  assert.match(moduleCss, /data-mod="payroll"[\s\S]*bottom: calc\(70px/);
});
