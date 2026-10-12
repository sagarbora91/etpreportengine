#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { zipSync, strToU8 } from 'fflate';

const root = path.resolve(import.meta.dirname, '..');
const evalDir = path.join(root, 'android', 'app', 'src', 'main', 'assets', 'public', '__etp_eval');
const androidTest = path.join(root, 'android', 'app', 'src', 'androidTest', 'java', 'com', 'saagartraders', 'bcc', 'EtpA1Api23EvaluationTest.java');
const action = process.argv[2] || 'stage';
if (action === 'clean') {
  fs.rmSync(evalDir, { recursive: true, force: true });
  fs.rmSync(androidTest, { force: true });
  process.stdout.write('[etp-a1] generated evaluation assets removed\n');
  process.exit(0);
}
if (action !== 'stage') throw new Error('Usage: node scripts/stage-etp-api23-evaluation.mjs [stage|clean]');
fs.mkdirSync(evalDir, { recursive: true });
fs.mkdirSync(path.dirname(androidTest), { recursive: true });
fs.copyFileSync(path.join(root, 'verification', 'etp-a1', 'EtpA1Api23EvaluationTest.java'), androidTest);

const rows = 5001;
let sheet = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="A1:D' + rows + '"/><sheetData>';
sheet += '<row r="1"><c r="A1" t="inlineStr"><is><t>INVNUMBER</t></is></c><c r="B1" t="inlineStr"><is><t>STORE_CODE</t></is></c><c r="C1" t="inlineStr"><is><t>TRANS_TYPE</t></is></c><c r="D1" t="inlineStr"><is><t>NETAMOUNT</t></is></c></row>';
for (let row = 2; row <= rows; row += 1) sheet += '<row r="' + row + '"><c r="A' + row + '" t="inlineStr"><is><t>000' + row + '</t></is></c><c r="B' + row + '" t="inlineStr"><is><t>WLMHW</t></is></c><c r="C' + row + '" t="inlineStr"><is><t>INV</t></is></c><c r="D' + row + '"><v>1.25</v></c></row>';
sheet += '</sheetData></worksheet>';
const files = {
  '[Content_Types].xml': strToU8('<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>'),
  '_rels/.rels': strToU8('<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>'),
  'xl/workbook.xml': strToU8('<?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Import" sheetId="1" r:id="rId1"/></sheets></workbook>'),
  'xl/_rels/workbook.xml.rels': strToU8('<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>'),
  'xl/worksheets/sheet1.xml': strToU8(sheet)
};
fs.writeFileSync(path.join(evalDir, 'synthetic-r022.xlsx'), zipSync(files, { level: 6 }));
fs.copyFileSync(path.join(root, 'node_modules', 'read-excel-file', 'bundle', 'read-excel-file.min.old.js'), path.join(evalDir, 'parser.js'));
fs.copyFileSync(path.join(root, 'www', 'etp-xlsx-preflight.js'), path.join(evalDir, 'preflight.js'));
fs.writeFileSync(path.join(evalDir, 'index.html'), `<!doctype html><meta charset="utf-8"><script src="preflight.js"></script><script src="parser.js"></script><script>
window.__etpA1={state:'running'};(function(){var gaps=[],last=performance.now(),beat=setInterval(function(){var now=performance.now();gaps.push(now-last-50);last=now;},50),start=performance.now();fetch('synthetic-r022.xlsx').then(function(r){return r.arrayBuffer();}).then(function(bytes){var container=SaagarEtpXlsxPreflight.inspect(bytes);if(!container.ok)throw new Error(container.code);return readXlsxFile(bytes).then(function(sheets){clearInterval(beat);var data=sheets[0].data,elapsed=Math.round(performance.now()-start),max=0;gaps.forEach(function(x){if(x>max)max=x;});window.__etpA1={state:'done',code:'XLSX_EVAL_OK',rows:data.length-1,columns:data[0].length,leadingZeroPreserved:typeof data[1][0]==='string'&&data[1][0].indexOf('000')===0,elapsedMs:elapsed,maxHeartbeatGapMs:Math.round(max),entryCount:container.entryCount};});}).catch(function(){clearInterval(beat);window.__etpA1={state:'done',code:'XLSX_PARSER_INTERNAL'};});})();
</script>`);
process.stdout.write('[etp-a1] staged generated API-23 evaluation assets; never ship this build\n');
