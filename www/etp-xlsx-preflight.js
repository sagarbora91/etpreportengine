/* ETP-A1 bounded OOXML container preflight. Pure/no-write and not app-loaded. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpXlsxPreflight = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  var LIMITS = Object.freeze({
    profileVersion: 1, maxInputBytes: 32 * 1024 * 1024, maxEntries: 512,
    maxEntryBytes: 32 * 1024 * 1024, maxTotalBytes: 128 * 1024 * 1024,
    maxCompressionRatio: 100, maxSheets: 8, maxRowsPerSheet: 250000,
    maxColumns: 128, maxCells: 2000000, maxCellText: 4096,
    maxSharedStrings: 250000, maxSharedStringsBytes: 16 * 1024 * 1024
  });
  function refusal(code, stage) { return Object.freeze({ ok: false, code: code, stage: stage || 'container', profileVersion: LIMITS.profileVersion }); }
  function viewOf(input) {
    if (input instanceof Uint8Array) return input;
    if (typeof ArrayBuffer !== 'undefined' && input instanceof ArrayBuffer) return new Uint8Array(input);
    return null;
  }
  function u16(bytes, at) { return bytes[at] | (bytes[at + 1] << 8); }
  function u32(bytes, at) { return (bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24)) >>> 0; }
  function nameOf(bytes, at, length) {
    var value = '';
    for (var index = 0; index < length; index += 1) {
      var code = bytes[at + index];
      if (code < 32 || code > 126) return '';
      value += String.fromCharCode(code);
    }
    return value.replace(/\\/g, '/');
  }
  function unsafeName(name) {
    return !name || name.length > 240 || /^(?:\/|[A-Za-z]:|\\\\)/.test(name) || /(?:^|\/)\.\.(?:\/|$)/.test(name) || /\0/.test(name);
  }
  function textOf(value) {
    if (typeof value === 'string') return value;
    var bytes = viewOf(value);
    if (!bytes) return null;
    if (typeof TextDecoder !== 'undefined') return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
    var output = '';
    for (var index = 0; index < bytes.length; index += 1) output += String.fromCharCode(bytes[index]);
    return decodeURIComponent(escape(output));
  }
  function attr(tag, name) {
    var match = new RegExp('(?:^|\\s)' + name.replace(':', '\\:') + '\\s*=\\s*["\\\']([^"\\\']*)["\\\']', 'i').exec(tag);
    return match ? match[1] : '';
  }
  function columnNumber(reference) {
    var match = /^([A-Z]+)[1-9][0-9]*$/i.exec(reference || '');
    if (!match) return 0;
    var value = 0, letters = match[1].toUpperCase();
    for (var index = 0; index < letters.length; index += 1) value = value * 26 + letters.charCodeAt(index) - 64;
    return value;
  }
  function rowNumber(reference) {
    var match = /^[A-Z]+([1-9][0-9]*)$/i.exec(reference || '');
    return match ? Number(match[1]) : 0;
  }
  function forbiddenXml(xml) { return /<!DOCTYPE|<!ENTITY/i.test(xml); }
  /* Second-stage inspection accepts only already-decompressed XML parts. The caller
     must obtain them from the same bytes admitted by inspect(); this API performs no I/O. */
  function inspectParts(parts) {
    if (!parts || typeof parts !== 'object') return refusal('XLSX_XML_PARTS_INVALID', 'xml');
    var normalized = Object.create(null), key;
    try {
      for (key in parts) if (Object.prototype.hasOwnProperty.call(parts, key)) {
        var folded = String(key).replace(/\\/g, '/').toLowerCase();
        if (unsafeName(folded) || normalized[folded] !== undefined) return refusal('XLSX_XML_PARTS_INVALID', 'xml');
        var xml = textOf(parts[key]);
        if (xml === null) return refusal('XLSX_XML_PARTS_INVALID', 'xml');
        if (forbiddenXml(xml)) return refusal('XLSX_XML_DTD_FORBIDDEN', 'xml');
        normalized[folded] = xml;
      }
    } catch (_) { return refusal('XLSX_XML_ENCODING_INVALID', 'xml'); }
    var workbook = normalized['xl/workbook.xml'];
    var relationships = normalized['xl/_rels/workbook.xml.rels'];
    if (workbook === undefined || relationships === undefined) return refusal('XLSX_XML_REQUIRED_PART_MISSING', 'xml');
    for (key in normalized) if (/\.rels$/.test(key) && /\bTargetMode\s*=\s*["']External["']/i.test(normalized[key])) return refusal('XLSX_EXTERNAL_RELATIONSHIP_FORBIDDEN', 'xml');
    var sheetTags = workbook.match(/<(?:[A-Za-z_][\w.-]*:)?sheet\b[^>]*>/gi) || [];
    if (!sheetTags.length || sheetTags.length > LIMITS.maxSheets) return refusal('XLSX_SHEET_COUNT_INVALID', 'xml');
    var ids = Object.create(null), names = Object.create(null);
    for (var sheetIndex = 0; sheetIndex < sheetTags.length; sheetIndex += 1) {
      var sheetTag = sheetTags[sheetIndex], id = attr(sheetTag, 'r:id'), sheetName = attr(sheetTag, 'name').toLowerCase(), state = attr(sheetTag, 'state').toLowerCase();
      if (!id || !sheetName || ids[id] || names[sheetName]) return refusal('XLSX_SHEET_IDENTITY_AMBIGUOUS', 'xml');
      if (state && state !== 'visible') return refusal('XLSX_HIDDEN_SHEET_FORBIDDEN', 'xml');
      ids[id] = true; names[sheetName] = true;
    }
    var relationTags = relationships.match(/<Relationship\b[^>]*>/gi) || [], mapped = Object.create(null);
    for (var relationIndex = 0; relationIndex < relationTags.length; relationIndex += 1) {
      var relationTag = relationTags[relationIndex], relationId = attr(relationTag, 'Id'), target = attr(relationTag, 'Target').replace(/^\//, '').toLowerCase();
      if (ids[relationId]) {
        if (mapped[relationId] || !/^(?:xl\/)?worksheets\/sheet[0-9]+\.xml$/.test(target)) return refusal('XLSX_SHEET_RELATIONSHIP_INVALID', 'xml');
        mapped[relationId] = target.indexOf('xl/') === 0 ? target : 'xl/' + target;
      }
    }
    for (key in ids) if (!mapped[key] || normalized[mapped[key]] === undefined) return refusal('XLSX_SHEET_RELATIONSHIP_INVALID', 'xml');
    var totalCells = 0, totalDeclaredCells = 0;
    for (key in mapped) {
      var worksheet = normalized[mapped[key]];
      if (/<(?:[A-Za-z_][\w.-]*:)?f(?:\s|>)/i.test(worksheet)) return refusal('XLSX_FORMULA_FORBIDDEN', 'xml');
      var dimensions = worksheet.match(/<(?:[A-Za-z_][\w.-]*:)?dimension\b[^>]*>/gi) || [];
      if (dimensions.length > 1) return refusal('XLSX_DIMENSION_INVALID', 'xml');
      if (dimensions.length === 1) {
        var range = attr(dimensions[0], 'ref').split(':'), last = range[range.length - 1];
        if (!rowNumber(last) || rowNumber(last) > LIMITS.maxRowsPerSheet || !columnNumber(last) || columnNumber(last) > LIMITS.maxColumns) return refusal('XLSX_DIMENSION_LIMIT_EXCEEDED', 'xml');
        var first = range[0], declaredRows = rowNumber(last) - rowNumber(first) + 1, declaredColumns = columnNumber(last) - columnNumber(first) + 1;
        if (range.length > 2 || declaredRows < 1 || declaredColumns < 1) return refusal('XLSX_DIMENSION_INVALID', 'xml');
        totalDeclaredCells += declaredRows * declaredColumns;
        if (totalDeclaredCells > LIMITS.maxCells) return refusal('XLSX_CELL_COUNT_EXCEEDED', 'xml');
      }
      var cells = worksheet.match(/<(?:[A-Za-z_][\w.-]*:)?c\b[^>]*>/gi) || [];
      totalCells += cells.length;
      if (totalCells > LIMITS.maxCells) return refusal('XLSX_CELL_COUNT_EXCEEDED', 'xml');
      for (var cellIndex = 0; cellIndex < cells.length; cellIndex += 1) {
        var reference = attr(cells[cellIndex], 'r');
        if (!rowNumber(reference) || rowNumber(reference) > LIMITS.maxRowsPerSheet || !columnNumber(reference) || columnNumber(reference) > LIMITS.maxColumns) return refusal('XLSX_CELL_REFERENCE_INVALID', 'xml');
      }
      var inlineTexts = worksheet.match(/<(?:[A-Za-z_][\w.-]*:)?t(?:\s[^>]*)?>([\s\S]*?)<\/(?:[A-Za-z_][\w.-]*:)?t>/gi) || [];
      for (var textIndex = 0; textIndex < inlineTexts.length; textIndex += 1) if (inlineTexts[textIndex].replace(/^<(?:[A-Za-z_][\w.-]*:)?t(?:\s[^>]*)?>|<\/(?:[A-Za-z_][\w.-]*:)?t>$/gi, '').length > LIMITS.maxCellText) return refusal('XLSX_CELL_TEXT_TOO_LONG', 'xml');
    }
    var shared = normalized['xl/sharedstrings.xml'];
    if (shared !== undefined) {
      if (shared.length > LIMITS.maxSharedStringsBytes) return refusal('XLSX_SHARED_STRINGS_TOO_LARGE', 'xml');
      var strings = shared.match(/<(?:[A-Za-z_][\w.-]*:)?si(?:\s[^>]*)?>[\s\S]*?<\/(?:[A-Za-z_][\w.-]*:)?si>/gi) || [];
      if (strings.length > LIMITS.maxSharedStrings) return refusal('XLSX_SHARED_STRING_COUNT_EXCEEDED', 'xml');
      for (var stringIndex = 0; stringIndex < strings.length; stringIndex += 1) {
        var content = '', textMatches = strings[stringIndex].match(/<(?:[A-Za-z_][\w.-]*:)?t(?:\s[^>]*)?>([\s\S]*?)<\/(?:[A-Za-z_][\w.-]*:)?t>/gi) || [];
        for (var partIndex = 0; partIndex < textMatches.length; partIndex += 1) content += textMatches[partIndex].replace(/^<(?:[A-Za-z_][\w.-]*:)?t(?:\s[^>]*)?>|<\/(?:[A-Za-z_][\w.-]*:)?t>$/gi, '');
        if (content.length > LIMITS.maxCellText) return refusal('XLSX_CELL_TEXT_TOO_LONG', 'xml');
      }
    }
    return Object.freeze({ ok: true, code: 'XLSX_XML_ACCEPTED', stage: 'xml', profileVersion: LIMITS.profileVersion, sheetCount: sheetTags.length, cellCount: totalCells });
  }
  function inspect(input) {
    var bytes = viewOf(input);
    if (!bytes) return refusal('XLSX_SIGNATURE_INVALID', 'input');
    if (bytes.length > LIMITS.maxInputBytes) return refusal('XLSX_INPUT_TOO_LARGE', 'input');
    if (bytes.length < 22 || u32(bytes, 0) !== 0x04034b50) return refusal('XLSX_SIGNATURE_INVALID', 'signature');
    var lower = Math.max(0, bytes.length - 65557), eocd = -1;
    for (var cursor = bytes.length - 22; cursor >= lower; cursor -= 1) if (u32(bytes, cursor) === 0x06054b50) { eocd = cursor; break; }
    if (eocd < 0) return refusal('XLSX_CONTAINER_MALFORMED');
    if (u16(bytes, eocd + 4) !== 0 || u16(bytes, eocd + 6) !== 0) return refusal('XLSX_CONTAINER_MALFORMED');
    var entries = u16(bytes, eocd + 10), centralBytes = u32(bytes, eocd + 12), centralAt = u32(bytes, eocd + 16);
    if (entries === 0xffff || centralBytes === 0xffffffff || centralAt === 0xffffffff) return refusal('XLSX_ZIP64_UNSUPPORTED');
    if (entries > LIMITS.maxEntries) return refusal('XLSX_ENTRY_COUNT_EXCEEDED');
    if (centralAt + centralBytes > eocd || centralAt < 0) return refusal('XLSX_CONTAINER_MALFORMED');
    var names = Object.create(null), required = Object.create(null), total = 0, position = centralAt;
    for (var entryIndex = 0; entryIndex < entries; entryIndex += 1) {
      if (position + 46 > eocd || u32(bytes, position) !== 0x02014b50) return refusal('XLSX_CONTAINER_MALFORMED');
      var flags = u16(bytes, position + 8), method = u16(bytes, position + 10);
      var compressed = u32(bytes, position + 20), expanded = u32(bytes, position + 24);
      var nameLength = u16(bytes, position + 28), extraLength = u16(bytes, position + 30), commentLength = u16(bytes, position + 32);
      var localAt = u32(bytes, position + 42), name = nameOf(bytes, position + 46, nameLength), folded = name.toLowerCase();
      if (flags & 1) return refusal('XLSX_ENCRYPTED');
      if (method !== 0 && method !== 8) return refusal('XLSX_COMPRESSION_UNSUPPORTED');
      if (unsafeName(name)) return refusal('XLSX_PATH_UNSAFE');
      if (names[folded]) return refusal('XLSX_ENTRY_DUPLICATE');
      names[folded] = true;
      if (expanded > LIMITS.maxEntryBytes) return refusal('XLSX_ENTRY_TOO_LARGE');
      total += expanded;
      if (total > LIMITS.maxTotalBytes) return refusal('XLSX_UNCOMPRESSED_TOTAL_EXCEEDED');
      if (expanded && (!compressed || expanded / compressed > LIMITS.maxCompressionRatio)) return refusal('XLSX_COMPRESSION_RATIO_EXCEEDED');
      if (/^(?:xl\/vbaProject\.bin|xl\/externalLinks\/|xl\/activeX\/|xl\/embeddings\/|customXml\/)/i.test(name)) return refusal('XLSX_ACTIVE_CONTENT_FORBIDDEN');
      if (localAt + 30 > centralAt || u32(bytes, localAt) !== 0x04034b50 || u16(bytes, localAt + 6) !== flags || u16(bytes, localAt + 8) !== method) return refusal('XLSX_ENTRY_METADATA_MISMATCH');
      var localNameLength = u16(bytes, localAt + 26), localExtraLength = u16(bytes, localAt + 28), localName = nameOf(bytes, localAt + 30, localNameLength);
      if (localName !== name) return refusal('XLSX_ENTRY_METADATA_MISMATCH');
      if (flags & 8) {
        if (u32(bytes, localAt + 18) !== 0 || u32(bytes, localAt + 22) !== 0) return refusal('XLSX_ENTRY_METADATA_MISMATCH');
        var descriptorAt = localAt + 30 + localNameLength + localExtraLength + compressed;
        if (u32(bytes, descriptorAt) === 0x08074b50) descriptorAt += 4;
        if (descriptorAt + 12 > centralAt || u32(bytes, descriptorAt) !== u32(bytes, position + 16) || u32(bytes, descriptorAt + 4) !== compressed || u32(bytes, descriptorAt + 8) !== expanded) return refusal('XLSX_ENTRY_METADATA_MISMATCH');
      } else if (u32(bytes, localAt + 18) !== compressed || u32(bytes, localAt + 22) !== expanded) return refusal('XLSX_ENTRY_METADATA_MISMATCH');
      if (folded === '[content_types].xml' || folded === '_rels/.rels' || folded === 'xl/workbook.xml') required[folded] = true;
      position += 46 + nameLength + extraLength + commentLength;
    }
    if (position !== centralAt + centralBytes) return refusal('XLSX_CONTAINER_MALFORMED');
    if (!required['[content_types].xml'] || !required['_rels/.rels'] || !required['xl/workbook.xml']) return refusal('XLSX_REQUIRED_PART_MISSING');
    return Object.freeze({ ok: true, code: 'XLSX_CONTAINER_ACCEPTED', stage: 'container', profileVersion: LIMITS.profileVersion, inputBytes: bytes.length, entryCount: entries, declaredUncompressedBytes: total });
  }
  return Object.freeze({ LIMITS: LIMITS, inspect: inspect, inspectParts: inspectParts });
});
