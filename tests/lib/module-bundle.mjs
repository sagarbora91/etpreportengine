import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const wwwPath = path.resolve(here, '../../www');
const manifestPath = path.join(wwwPath, 'module-manifest.js');
const require = createRequire(import.meta.url);

export function loadModuleManifest() {
  return require(manifestPath);
}

export function loadModuleBundle() {
  return loadModuleManifest().modules.map(module => {
    if (!module.src) throw new Error('external module path missing: ' + module.id);
    const externalPath = path.resolve(wwwPath, module.src);
    const bytes = fs.readFileSync(externalPath);
    return {
      ...module,
      html: bytes.toString('utf8'),
      actualBytes: bytes.length,
      actualSha256: crypto.createHash('sha256').update(bytes).digest('hex')
    };
  });
}

export function inlineModuleScripts(html) {
  const withoutComments = html.replace(/<!--[\s\S]*?-->/g, '');
  const out = [];
  for (const match of withoutComments.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
    const attrs = match[1] || '';
    if (/\bsrc\s*=/.test(attrs)) continue;
    const type = attrs.match(/\btype\s*=\s*["']([^"']+)["']/i)?.[1]?.toLowerCase() || '';
    if (type && !/(?:java|ecma)script|module/.test(type)) continue;
    out.push(match[2]);
  }
  return out;
}
