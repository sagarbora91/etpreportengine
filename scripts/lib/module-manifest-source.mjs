import fs from 'node:fs';
import path from 'node:path';

export const MANIFEST_START = '/*__SAAGAR_MODULE_MANIFEST_START__*/';
export const MANIFEST_END = '/*__SAAGAR_MODULE_MANIFEST_END__*/';

export function readModuleManifestSource(root) {
  const filePath = path.join(root, 'www', 'module-manifest.js');
  const source = fs.readFileSync(filePath, 'utf8');
  const start = source.indexOf(MANIFEST_START);
  const end = source.indexOf(MANIFEST_END);
  if (start < 0 || end <= start) throw new Error('module manifest data markers not found');
  if (start !== source.lastIndexOf(MANIFEST_START) || end !== source.lastIndexOf(MANIFEST_END)) {
    throw new Error('module manifest data markers must be unique');
  }
  const dataStart = start + MANIFEST_START.length;
  const json = source.slice(dataStart, end).trim();
  return { filePath, source, data: JSON.parse(json), dataStart, dataEnd: end };
}

export function renderModuleManifestSource(snapshot, data) {
  const json = JSON.stringify(data, null, 2);
  return snapshot.source.slice(0, snapshot.dataStart) + json + snapshot.source.slice(snapshot.dataEnd);
}
