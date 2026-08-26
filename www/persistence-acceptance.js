/* DAT-02 permanent acceptance contract. Pure calculations live here so the
   p95 budgets are regression-tested; storage-core supplies real device samples. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarPersistenceAcceptance = api;
})(typeof window !== 'undefined' ? window : globalThis, function () {
  'use strict';
  var THRESHOLDS = { samples: 5, exportP95Ms: 150, frameGapP95Ms: 250, totalP95Ms: 3000 };
  function finite(value) { var n = Number(value); return Number.isFinite(n) && n >= 0 ? n : NaN; }
  function p95(values) {
    var nums = (values || []).map(finite).filter(Number.isFinite).sort(function (a, b) { return a - b; });
    if (!nums.length) return Infinity;
    return nums[Math.max(0, Math.ceil(nums.length * 0.95) - 1)];
  }
  function evaluate(samples, thresholds) {
    thresholds = Object.assign({}, THRESHOLDS, thresholds || {});
    samples = Array.isArray(samples) ? samples.slice() : [];
    var complete = samples.length >= thresholds.samples && samples.every(function (s) {
      return s && s.ok === true && Number.isFinite(finite(s.exportMs)) && Number.isFinite(finite(s.frameGapMs)) && Number.isFinite(finite(s.totalMs));
    });
    var metrics = {
      exportP95Ms: p95(samples.map(function (s) { return s && s.exportMs; })),
      frameGapP95Ms: p95(samples.map(function (s) { return s && s.frameGapMs; })),
      totalP95Ms: p95(samples.map(function (s) { return s && s.totalMs; }))
    };
    return {
      contract: 'DAT-02-v1',
      at: new Date().toISOString(),
      accepted: complete && metrics.exportP95Ms <= thresholds.exportP95Ms && metrics.frameGapP95Ms <= thresholds.frameGapP95Ms && metrics.totalP95Ms <= thresholds.totalP95Ms,
      complete: complete,
      sampleCount: samples.length,
      thresholds: thresholds,
      metrics: metrics,
      samples: samples.map(function (s) { return { ok: s.ok === true, exportMs: finite(s.exportMs), frameGapMs: finite(s.frameGapMs), totalMs: finite(s.totalMs) }; })
    };
  }
  return { THRESHOLDS: THRESHOLDS, p95: p95, evaluate: evaluate };
});