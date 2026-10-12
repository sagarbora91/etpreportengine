import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { transformSync } from '@babel/core';

const root = path.resolve(import.meta.dirname, '..');
const publicDir = path.join(root, 'android', 'app', 'src', 'main', 'assets', 'public');
const androidAssetsDir = path.dirname(publicDir);
const sourceDir = path.join(root, 'www');
const require = createRequire(import.meta.url);
const manifest = require(path.join(sourceDir, 'module-manifest.js'));

export const LEGACY_WEBVIEW_PRELUDE = `(function(){'use strict';
if(typeof window.globalThis==='undefined')window.globalThis=window;
var legacyRoot=document.documentElement,legacyClass='saagar-legacy-webview',chromeMatch=String(navigator.userAgent||'').match(/(?:Chrome|CriOS)\\/(\\d+)/),legacyWebView=chromeMatch?parseInt(chromeMatch[1],10)<57:!(window.CSS&&CSS.supports&&CSS.supports('display','grid'));
if(legacyWebView&&(' '+legacyRoot.className+' ').indexOf(' '+legacyClass+' ')<0)legacyRoot.className=(legacyRoot.className?legacyRoot.className+' ':'')+legacyClass;
if(!Array.from)Array.from=function(x){return Array.prototype.slice.call(x);};
if(!Array.prototype.includes)Array.prototype.includes=function(x,n){return this.indexOf(x,n||0)!==-1;};
if(!Array.prototype.find)Array.prototype.find=function(fn,t){for(var i=0;i<this.length;i++)if(fn.call(t,this[i],i,this))return this[i];};
if(!Array.prototype.findIndex)Array.prototype.findIndex=function(fn,t){for(var i=0;i<this.length;i++)if(fn.call(t,this[i],i,this))return i;return -1;};
if(!Array.prototype.flatMap)Array.prototype.flatMap=function(fn,t){return Array.prototype.concat.apply([],this.map(fn,t));};
if(!String.prototype.includes)String.prototype.includes=function(x,n){return this.indexOf(x,n||0)!==-1;};
if(!String.prototype.startsWith)String.prototype.startsWith=function(x,n){return this.substr(n||0,x.length)===x;};
if(!String.prototype.endsWith)String.prototype.endsWith=function(x,n){var p=n===undefined?this.length:n;return this.substring(p-x.length,p)===x;};
if(!String.prototype.padStart)String.prototype.padStart=function(n,p){p=String(p===undefined?' ':p);var s=String(this);while(s.length<n)s=p+s;return s.slice(-n);};
if(!Object.entries)Object.entries=function(o){return Object.keys(o).map(function(k){return[k,o[k]];});};
if(!Object.values)Object.values=function(o){return Object.keys(o).map(function(k){return o[k];});};
if(!Object.fromEntries)Object.fromEntries=function(xs){var o={};xs.forEach(function(x){o[x[0]]=x[1];});return o;};
if(!Object.assign)Object.assign=function(t){if(t==null)throw new TypeError('Object.assign target');for(var i=1;i<arguments.length;i++){var s=arguments[i];if(s!=null)Object.keys(Object(s)).forEach(function(k){t[k]=s[k];});}return t;};
if(!Number.isFinite)Number.isFinite=function(x){return typeof x==='number'&&isFinite(x);};
if(!Number.isInteger)Number.isInteger=function(x){return Number.isFinite(x)&&Math.floor(x)===x;};
if(window.NodeList&&!NodeList.prototype.forEach)NodeList.prototype.forEach=Array.prototype.forEach;
if(window.HTMLCollection&&!HTMLCollection.prototype.forEach)HTMLCollection.prototype.forEach=Array.prototype.forEach;
if(window.Element&&!Element.prototype.replaceChildren)Element.prototype.replaceChildren=function(){while(this.firstChild)this.removeChild(this.firstChild);for(var i=0;i<arguments.length;i++){var child=arguments[i];this.appendChild(child&&typeof child==='object'&&typeof child.nodeType==='number'?child:document.createTextNode(String(child)));}};
})();`;

function collectCssVariables(source, target) {
  for (const match of source.matchAll(/--([A-Za-z0-9_-]+)\s*:\s*([^;{}]+);/g)) {
    if (!target.has(match[1])) target.set(match[1], match[2].trim());
  }
}

export function resolveCssVariables(source, variables) {
  let output = source;
  for (let pass = 0; pass < 8; pass++) {
    let changed = false;
    output = output.replace(/var\(\s*--([A-Za-z0-9_-]+)\s*(?:,\s*([^()]*))?\)/g, (whole, name, fallback) => {
      const value = variables.get(name) || (fallback && fallback.trim());
      if (!value) return whole;
      changed = true;
      return value;
    });
    if (!changed) break;
  }
  return output;
}

/* CSS custom properties must be resolved only in HTML/CSS markup. Applying the
   substitution to the whole HTML document also rewrites JavaScript string
   literals. A font token such as `'DM Serif Display', Georgia, serif` then
   injects unescaped quotes into a single-quoted HTML template and makes the
   entire module a syntax error on Chrome 44. Keep every script byte opaque and
   resolve variables only in the surrounding markup and style blocks. */
function resolveHtmlCssVariables(source, variables) {
  return source.split(/(<script\b[\s\S]*?<\/script\s*>)/gi)
    .map(segment => /^<script\b/i.test(segment) ? segment : resolveCssVariables(segment, variables))
    .join('');
}

function babel(source, filename) {
  return transformSync(source, {
    filename,
    sourceType: 'script',
    presets: [['@babel/preset-env', { targets: { chrome: '44' }, bugfixes: true, modules: false }]],
    comments: false,
    compact: false,
    sourceMaps: false
  }).code;
}

export function transformJavaScriptAsset(source, filename, relativePath) {
  // This file is the canonical package/version authority consumed by the audit
  // and release tooling. It already uses Chrome-44-compatible syntax, so changing
  // its bytes adds no compatibility value and breaks the generated identity hash
  // contract. Scope the exception to the exact root asset; similarly named nested
  // files remain ordinary application JavaScript and are still down-levelled.
  if (relativePath === 'build-identity.js') return source;
  return babel(source, filename);
}

export function transformHtml(source, filename, cssVariables) {
  let count = 0;
  const transformed = source.replace(/<!--[\s\S]*?-->|<script(?![^>]*\bsrc=)([^>]*)>([\s\S]*?)<\/script>/gi, (whole, attrs, body) => {
    if (whole.startsWith('<!--')) return whole;
    if (/\btype\s*=\s*["'](?:application\/json|text\/template)["']/i.test(attrs) || !body.trim()) return whole;
    count += 1;
    return `<script${attrs}>\n${babel(body, `${filename}#inline-${count}`)}\n</script>`;
  });
  return resolveHtmlCssVariables(transformed
    .replace(/x=>x\.checked=true/g, 'function(x){x.checked=true;}')
    .replace(/<head([^>]*)>/i, `<head$1>\n<script>${LEGACY_WEBVIEW_PRELUDE}</script>`), cssVariables);
}

function filesUnder(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    return entry.isDirectory() ? filesUnder(full) : [full];
  });
}

export function prepareApi23Assets() {
  if (!fs.existsSync(publicDir)) throw new Error('Generated Android public assets are missing; run capacitor sync first');

  // Capacitor injects this asset before the document executes. Capacitor 6 ships
  // modern syntax even though Android still permits API 23, so the bridge must be
  // down-levelled alongside the application or native plugins silently disappear.
  const bridgeSource = path.join(root, 'node_modules', '@capacitor', 'android', 'capacitor', 'src', 'main', 'assets', 'native-bridge.js');
  fs.writeFileSync(
    path.join(androidAssetsDir, 'native-bridge.js'),
    babel(fs.readFileSync(bridgeSource, 'utf8'), bridgeSource),
    'utf8'
  );

  const manifestPath = path.join(publicDir, 'module-manifest.js');
  const deferred = new Set([manifestPath]);
  const generatedFiles = filesUnder(publicDir);
  const cssVariables = new Map();
  for (const file of generatedFiles) {
    const ext = path.extname(file).toLowerCase();
    if (ext === '.css' || ext === '.html') collectCssVariables(fs.readFileSync(file, 'utf8'), cssVariables);
  }
  for (const file of generatedFiles) {
    if (deferred.has(file)) continue;
    const ext = path.extname(file).toLowerCase();
    const relativePath = path.relative(publicDir, file).split(path.sep).join('/');
    if (ext === '.js') fs.writeFileSync(
      file,
      transformJavaScriptAsset(fs.readFileSync(file, 'utf8'), file, relativePath),
      'utf8'
    );
    if (ext === '.css') fs.writeFileSync(file, resolveCssVariables(fs.readFileSync(file, 'utf8'), cssVariables), 'utf8');
    if (ext === '.html') fs.writeFileSync(file, transformHtml(fs.readFileSync(file, 'utf8'), file, cssVariables), 'utf8');
  }

  const canonicalIdentity = fs.readFileSync(path.join(sourceDir, 'build-identity.js'));
  const generatedIdentity = fs.readFileSync(path.join(publicDir, 'build-identity.js'));
  if (!generatedIdentity.equals(canonicalIdentity)) {
    throw new Error('Generated Android build identity must remain byte-identical to canonical authority');
  }

  let generatedManifest = fs.readFileSync(manifestPath, 'utf8');
  const pinned = [...manifest.modules, ...manifest.sharedAssets];
  for (const item of pinned) {
    const transformedPath = path.join(publicDir, item.file.replace(/\//g, path.sep));
    const bytes = fs.readFileSync(transformedPath);
    const hash = crypto.createHash('sha256').update(bytes).digest('hex');
    generatedManifest = generatedManifest.replace(`bytes: ${item.bytes}`, `bytes: ${bytes.length}`);
    generatedManifest = generatedManifest.replace(`sha256: '${item.sha256}'`, `sha256: '${hash}'`);
  }
  fs.writeFileSync(manifestPath, babel(generatedManifest, manifestPath), 'utf8');
  process.stdout.write(`[api23] prepared ${filesUnder(publicDir).length} Android assets for Chrome 44\n`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  prepareApi23Assets();
}
