/* Shared Retail ETP profile. App-loaded, pure and no-write. */
(function (root, factory) {
  var api = factory(root && root.SaagarEtpImportFoundation);
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarEtpRetailProfile = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function (foundation) {
  'use strict';
  if (!foundation && typeof require === 'function') foundation = require('./etp-import-foundation.js');
  var ETP_PROFILE_VERSION = 'retail-etp-core-v1';
  var IDENTIFIER_POLICY = Object.freeze({ mode: 'EXACT_XLSX_INTEGER_TEXT', maxDigits: 32, leadingZeroRepair: false });
  var STORES = Object.freeze(['WLMHW', 'HEMW']);
  var COMMON = [
    ['TRANS_TYPE', 'transactionTypeRaw'], ['STORE CODE', 'storeCode'],
    ['STORE NAME', 'storeName'], ['STORE TYPE', 'storeType'], ['CHANNEL', 'channel'],
    ['REGION', 'region'], ['CITY', 'city']
  ];
  var DEFINITIONS = {
    R003: {
      aliases: ['ALL DISCOUNT TYPE'], date: 'INVOICE DATE',
      identifiers: ['invoiceNumber', 'itemNumber'], measures: ['quantity', 'netAmount', 'netValue'],
      columns: COMMON.concat([
        ['INVOICE NUMBER','invoiceNumber'],['INVOICE DATE','invoiceDate'],['ITEMNUMBER','itemNumber'],
        ['BRAND','brand'],['BRAND NAME','brandName'],['CLUSTER','cluster'],['GENDER','gender'],
        ['QTY','quantity'],['UCP','ucp'],['GROSSUCP','grossUcp'],['SCH_DISCOUNTS','schemeDiscount'],
        ['NETGROSS','netGross'],['ACTIVATION DETAILS','activationDetails'],['USER DISCOUNTS','userDiscount'],
        ['OTHERCHARG','otherCharges'],['NETAMOUNT','netAmount'],['USER DISCOUNT DETAILS','userDiscountDetails'],
        ['TAX','taxAmount'],['NETVALUE','netValue'],['INVOICE REF NO','invoiceRefNumber'],
        ['INVOICE REF DATE','invoiceRefDate'],
        ['EASTIMESTAMP','easTimestamp'],['STORETIMESTAMP','storeTimestamp']
      ]), pii: ['CUSTOMERNUMBER','CUSTOMERNAME','CONTACTNO','ULP NO']
    },
    R013: {
      aliases: ['CRO WISE SALES'], date: 'INVDATE',
      identifiers: ['invoiceNumber', 'itemNumber', 'croNumber'], measures: ['quantity', 'netAmount', 'netValue'],
      columns: COMMON.concat([
        ['ITEMNUMBER','itemNumber'],['BRAND','brand'],['BRANDNAME','brandName'],['CLUSTER','cluster'],
        ['GENDER','gender'],['CRO NUMBER','croNumber'],['INVNUMBER','invoiceNumber'],
        ['INVDATE','invoiceDate'],['QTY','quantity'],['UCP','ucp'],['GROSSUCP','grossUcp'],
        ['SCH_DISCOUNTS','schemeDiscount'],['NETGROSS','netGross'],['PRE_DISCOUNTS','preDiscount'],
        ['NETAMOUNT','netAmount'],['NETVALUE','netValue'],['INVREFNO','invoiceRefNumber'],['INVREFDATE','invoiceRefDate']
      ]), pii: ['CRO NAME','CUSTOMERNAME','CONTACTNO']
    },
    R022: {
      aliases: ['REVENUE REPORT'], date: 'INVOICEDATE',
      identifiers: ['invoiceNumber'], measures: ['invoiceQuantity', 'netValue'],
      columns: COMMON.concat([
        ['STATE','state'],['INVNUMBER','invoiceNumber'],['InvoiceQuantity','invoiceQuantity'],
        ['INVOICEDATE','invoiceDate'],['INVOICEYEAR','invoiceYear'],['CASH','cashAmount'],
        ['CARD','cardAmount'],['CHEQUE','chequeAmount'],['LOYALTY_POINTS','loyaltyPointsAmount'],
        ['GV','giftVoucherAmount'],['CREDITNOTE REDEEM','creditNoteRedeemedAmount'],['EXCESS GV','excessGvAmount'],
        ['ROUND OFF','roundOff'],['NO REFUND','noRefundAmount'],['OTHERS','othersAmount'],
        ['TATA GV','tataGvAmount'],['GIFTCARD','giftCardAmount'],['TataCliQ','tatacliqAmount'],
        ['GYFTR','gyftrAmount'],['PAYTM','paytmAmount'],['HELIOSOMNI','heliosOmniAmount'],
        ['ADVANCERDEEM','advanceRedeemAmount'],['BHIMUPI','bhimUpiAmount'],['PHONEPE','phonepeAmount'],
        ['BHARATPE','bharatpeAmount'],['BAJAJFIN','bajajFinanceAmount'],['RAZORPAY','razorpayAmount'],
        ['PAYMENTTYPE24','paymentType24Amount'],['PAYMENTTYPE25','paymentType25Amount'],
        ['ISSUED CREDITNOTE','issuedCreditNoteAmount'],['CASH REFUND','cashRefundAmount'],
        ['Cheque/RTGS REFUND','chequeRtgsRefundAmount'],['NetValue','netValue'],
        ['ENCIRCLE','encircleAmountOrFlag'],['STORETIMESTAMP','storeTimestamp'],
        ['REFERENCENUMBER','referenceNumber'],['REFERENCEYEAR','referenceYear']
      ]), pii: ['CUSTOMERNAME','ContactNo']
    },
    R025: {
      aliases: ['SDB VARIANTWISE SALES', 'SDB VARIANTWISESALES'], date: 'INVDATE',
      identifiers: ['invoiceNumber', 'itemNumber'], measures: ['quantity', 'netAmount', 'netValue'],
      columns: [
        ['TRANS_TYPE','transactionTypeRaw'],['STORE CODE','storeCode'],['STORENAME','storeName'],
        ['STORETYPE','storeType'],['CHANNEL','channel'],['REGION','region'],['CITY','city'],
        ['ITEMNUMBER','itemNumber'],['HSNCODE','hsnCode'],['BRAND','brand'],['BRANDNAME','brandName'],
        ['CLUSTER','cluster'],['GENDER','gender'],['INVNUMBER','invoiceNumber'],['INVDATE','invoiceDate'],
        ['QTY','quantity'],['UCP','ucp'],['GROSSUCP','grossUcp'],['SCH_DISCOUNTS','schemeDiscount'],
        ['USER_DISCOUNTS','userDiscount'],['HELIOS_CREDITNOTE','heliosCreditNoteAmount'],
        ['PROMO_GC','promoGcAmount'],['NETGROSS','netGross'],['PRE_DISCOUNTS','preDiscount'],
        ['NETAMOUNT','netAmount'],['SGST/UTGST %','sgstUtgstRate'],['SGST/UTGST VALUE','sgstUtgstValue'],
        ['CSGT %','cgstRate'],['CSGT VALUE','cgstValue'],['IGST %','igstRate'],['IGST VALUE','igstValue'],
        ['CESS %','cessRate'],['CESS VALUE','cessValue'],['TAX','taxAmount'],['NETVALUE','netValue'],
        ['INVREFNO','invoiceRefNumber'],['INVREFDATE','invoiceRefDate'],['STORETIMESTAMP','storeTimestamp']
      ], pii: ['CUSTOMERNAME','CONTACTNO','ULPNUMBER']
    }
  };
  function freezeDefinition(definition) {
    var fields = {}, headers = [], numericOutputs = [], numericTextOutputs = [];
    definition.columns.forEach(function (pair) { fields[pair[0]] = pair[1]; headers.push(pair[0]); });
    definition.columns.forEach(function (pair) {
      if (/(?:quantity|amount|value|discount|charges|gross|ucp|roundOff|rate|encircleAmountOrFlag)$/i.test(pair[1])) numericOutputs.push(pair[1]);
      if (/(?:number|year|code|timestamp)$/i.test(pair[1]) && pair[1] !== 'storeCode') numericTextOutputs.push(pair[1]);
    });
    definition.pii.forEach(function (header) { headers.push(header); });
    return Object.freeze({
      aliases: Object.freeze(definition.aliases.slice()), businessDateHeader: definition.date,
      fields: Object.freeze(fields), dropHeaders: Object.freeze(definition.pii.slice()),
      requiredIdentifiers: Object.freeze(definition.identifiers.slice()),
      numericIdentifierPolicy: IDENTIFIER_POLICY,
      numericTextOutputs: Object.freeze(numericTextOutputs),
      requiredMeasures: Object.freeze(definition.measures.slice()),
      numericOutputs: Object.freeze(numericOutputs),
      exactHeaders: Object.freeze(headers),
      signatureKey: foundation.normalizeHeaderSignature(headers).key
    });
  }
  var REPORTS = {};
  Object.keys(DEFINITIONS).forEach(function (id) { REPORTS[id] = freezeDefinition(DEFINITIONS[id]); });
  REPORTS = Object.freeze(REPORTS);
  function adapters() {
    var result = {};
    Object.keys(REPORTS).forEach(function (id) {
      var report = REPORTS[id];
      result[id] = { fields: report.fields, dropHeaders: report.dropHeaders,
        requiredIdentifiers: report.requiredIdentifiers, requiredMeasures: report.requiredMeasures,
        businessDateHeader: report.businessDateHeader };
    });
    return Object.freeze(result);
  }
  function signatures() {
    var result = {};
    Object.keys(REPORTS).forEach(function (id) { result[id] = REPORTS[id].exactHeaders; });
    return Object.freeze(result);
  }
  function normalizeFileAlias(value) {
    return String(value || '').split(/[\\/]/).pop().replace(/\.xlsx$/i, '')
      .replace(/^\d{12}_/, '').replace(/^RO(22|25)(?=[_ -])/i, 'R0$1')
      .replace(/^[RWH]\d{3}[_ -]+/i, '').split(/\s+-\s+/)[0]
      .toUpperCase().replace(/[^A-Z0-9]+/g, ' ').trim().replace(/\s+/g, ' ');
  }
  function detect(headers, fileLabel, selectedReportId) {
    var headerResult = foundation.detectReport(headers, signatures());
    if (!headerResult.ok) return headerResult;
    var detected = headerResult.reportId, selected = String(selectedReportId || '').trim().toUpperCase();
    if (selected && selected !== detected) return Object.freeze({ ok: false, code: 'REPORT_SELECTION_CONTRADICTS_HEADER', detectedReportId: detected });
    var alias = normalizeFileAlias(fileLabel), aliases = REPORTS[detected].aliases;
    if (alias && aliases.indexOf(alias) < 0) return Object.freeze({ ok: false, code: 'REPORT_FILENAME_CONTRADICTS_HEADER', detectedReportId: detected });
    return Object.freeze({ ok: true, code: 'REPORT_DETECTED', reportId: detected,
      signature: headerResult.signature, signatureKey: headerResult.signatureKey });
  }
  return Object.freeze({ VERSION: ETP_PROFILE_VERSION, ETP_PROFILE_VERSION: ETP_PROFILE_VERSION, STORES: STORES, REPORTS: REPORTS, IDENTIFIER_POLICY: IDENTIFIER_POLICY,
    adapters: adapters, signatures: signatures, normalizeFileAlias: normalizeFileAlias, detect: detect });
});
