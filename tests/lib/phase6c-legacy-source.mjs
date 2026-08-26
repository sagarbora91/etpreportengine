import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  LEGACY_ASSET,
  restoreMigratedLegacySource
} from '../../scripts/prepare-phase6c-mobile-legacy-css.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const authority = fs.readFileSync(path.join(root, 'tests/fixtures/phase6c/module-mobile-legacy.css'), 'utf8');
const commonLink = '<link rel="stylesheet" href="../../shared/module-mobile-common.css">\n';
const legacyLink = '<link id="st-v5-mobile-css" rel="stylesheet" href="../../shared/module-mobile-legacy.css">';

export function restoreInlineLegacySource(moduleId, source) {
  const linked = source.includes(legacyLink) ? source : source.replace(commonLink, commonLink + legacyLink);
  const restored = restoreMigratedLegacySource(moduleId, linked, authority);
  assert.notEqual(restored, source, `${moduleId} Phase 6C migrated source`);
  return restored;
}
