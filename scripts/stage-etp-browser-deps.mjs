#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import crypto from 'node:crypto';
import readExcelPackage from 'read-excel-file/package.json' with { type: 'json' };
import fflatePackage from 'fflate/package.json' with { type: 'json' };
const root=path.resolve(import.meta.dirname,'..'),vendor=path.join(root,'www','vendor');
const files=[
  {source:path.join(root,'node_modules','read-excel-file','bundle','read-excel-file.min.js'),target:path.join(vendor,'read-excel-file-9.3.7.min.js'),version:readExcelPackage.version,expected:'9.3.7'},
  {source:path.join(root,'node_modules','fflate','umd','index.js'),target:path.join(vendor,'fflate-0.8.3.min.js'),version:fflatePackage.version,expected:'0.8.3'}
];
fs.mkdirSync(vendor,{recursive:true});
for(const file of files){if(file.version!==file.expected)throw new Error(`ETP browser dependency version mismatch: expected ${file.expected}, got ${file.version}`);const bytes=fs.readFileSync(file.source);fs.writeFileSync(file.target,bytes);const hash=crypto.createHash('sha256').update(bytes).digest('hex');process.stdout.write(`[etp-browser-deps] ${path.basename(file.target)} ${bytes.length} bytes sha256=${hash}\n`);}
