#!/usr/bin/env node
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { readModuleManifestSource, renderModuleManifestSource } from './lib/module-manifest-source.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const snapshot = readModuleManifestSource(root);

function refresh(records) {
  return records.map(record => {
    const bytes = fs.readFileSync(path.join(root, 'www', record.file));
    return {
      ...record,
      bytes: bytes.length,
      sha256: crypto.createHash('sha256').update(bytes).digest('hex')
    };
  });
}

const data = {
  ...snapshot.data,
  modules: refresh(snapshot.data.modules),
  sharedAssets: refresh(snapshot.data.sharedAssets)
};
fs.writeFileSync(snapshot.filePath, renderModuleManifestSource(snapshot, data), 'utf8');
process.stdout.write(`${JSON.stringify({ modules: data.modules.length, sharedAssets: data.sharedAssets.length })}\n`);
