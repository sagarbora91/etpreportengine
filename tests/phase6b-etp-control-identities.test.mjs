import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import { restorePrePhase6h1EtpIndex } from './lib/phase6h1-etp-source.mjs';

const html = restorePrePhase6h1EtpIndex(fs.readFileSync(new URL('../www/modules/etp/index.html', import.meta.url), 'utf8'));
const staticMarkup = html.replace(/<(script|style|template)\b[^>]*>[\s\S]*?<\/\1\s*>/gi, '');
// Phase-6B facade wiring is part of the authorized module baseline; removing only
// identity annotations must reproduce every other byte of that baseline.
const originalSha256 = 'ca3e7b09da0cbc86f389b3472aba5ed3bb5050991be671063a39842de41651b6';

function values(attribute) {
  return [...staticMarkup.matchAll(new RegExp(`\\b${attribute}="([^"]+)"`, 'g'))].map(match => match[1]);
}

test('ETP freezes unique safe identities for all 14 existing static actions', () => {
  const actions = values('data-action');
  assert.deepEqual(actions, [
    'tab-import', 'tab-verified', 'tab-coverage', 'tab-reconciliation',
    'etp-import-form', 'etp-coverage-confirmed', 'etp-validate', 'etp-confirm',
    'select-verified-scope', 'refresh-verified-views', 'etp-history-refresh',
    'select-exceptions-scope', 'refresh-exceptions', 'st-v5-home-fab'
  ]);
  assert.equal(new Set(actions).size, 14);
  actions.forEach(action => assert.match(action, /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/));
});

test('ETP gives only the eight passive import and scope fields stable field identities', () => {
  const fields = values('data-field');
  assert.deepEqual(fields, [
    'store-code', 'financial-year', 'period-start', 'period-end',
    'report-r003', 'report-r013', 'report-r022', 'report-r025'
  ]);
  assert.equal(new Set(fields).size, 8);
  fields.forEach(field => assert.match(field, /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/));
});

test('ETP identity annotations leave every pre-existing byte and control semantic unchanged', () => {
  const withoutIdentityAnnotations = html
    .replace(/ data-action="[^"]+"/g, '')
    .replace(/ data-field="[^"]+"/g, '');
  assert.equal(
    crypto.createHash('sha256').update(withoutIdentityAnnotations).digest('hex'),
    originalSha256
  );
});
