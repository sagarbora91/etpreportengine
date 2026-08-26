import assert from 'node:assert/strict';
import test from 'node:test';
import { createRequire } from 'node:module';
const require=createRequire(import.meta.url);
const policy=require('../www/etp-xlsx-parser-policy.js');

test('candidate parseNumber adapter preserves numeric lexical text and source type',()=>{ const token=policy.numericLexical('00123'); assert.equal(token.etpCellKind,'numeric'); assert.equal(token.lexical,'00123'); assert.equal(policy.isNumericToken(token),true); assert.throws(()=>policy.numericLexical('1,23'),/XLSX_NUMERIC_LEXICAL_INVALID/); });
test('text identifiers including leading zeros remain admissible',()=>{ const out=policy.inspectTable([['INVNUMBER','STORE_CODE'],['000123','WLMHW']],['INVNUMBER']); assert.deepEqual(out,{ok:true,code:'XLSX_TABLE_ACCEPTED',stage:'table',rows:1,columns:2,nonblankCells:2}); });
test('numeric required identifiers fail closed without an explicit lexical policy',()=>{ const out=policy.inspectTable([['INVNUMBER','STORE_CODE'],[policy.numericLexical('123'),'WLMHW']],['INVNUMBER']); assert.equal(out.ok,false); assert.equal(out.code,'XLSX_IDENTIFIER_NUMERIC_UNVERIFIED'); });
test('approved XLSX integer lexical forms canonicalize without numeric conversion',()=>{ const rule={mode:'EXACT_XLSX_INTEGER_TEXT',maxDigits:32}; assert.equal(policy.identifierText(policy.numericLexical('1.2345E+16'),rule),'12345000000000000'); assert.equal(policy.identifierText(policy.numericLexical('2026.0'),rule),'2026'); assert.equal(policy.identifierText(policy.numericLexical('0.0'),rule),'0'); });
test('fractional, signed, padded and over-limit identifier forms remain refused',()=>{ const rule={mode:'EXACT_XLSX_INTEGER_TEXT',maxDigits:5}; for(const value of ['1.25','-1','00123','1e6','1e-2']) assert.equal(policy.identifierText(policy.numericLexical(value),rule),null,value); });
test('untracked native numbers fail before business policy',()=>{ const out=policy.inspectTable([['INVNUMBER','NETAMOUNT'],['A1',12.5]],['INVNUMBER']); assert.equal(out.code,'XLSX_NUMERIC_TYPE_UNTRACKED'); });
test('duplicate headers, missing identifiers and unsupported values refuse deterministically',()=>{ assert.equal(policy.inspectTable([['A','A'],['x','y']],['A']).code,'XLSX_HEADER_INVALID'); assert.equal(policy.inspectTable([['A'],['x']],['INVNUMBER']).code,'XLSX_HEADER_INVALID'); assert.equal(policy.inspectTable([['INVNUMBER'],[{}]],['INVNUMBER']).code,'XLSX_CELL_TYPE_UNSUPPORTED'); });
