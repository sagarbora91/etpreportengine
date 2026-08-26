/* Per-module Owner PIN policy.
   Module entry protection is deliberately OFF by default. This policy is
   separate from role access and from one-use reauthentication for high-risk
   actions inside a module. */
(function (root, factory) {
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.SaagarModulePinPolicy = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var POLICY_VERSION = 1;
  var MODULE_IDS = Object.freeze([
    'stock',
    'service',
    'qms',
    'dsr',
    'expense',
    'grooming',
    'cro_audit',
    'payroll',
    'leave',
    'tax',
    'planning',
    'etp'
  ]);

  function emptyModules() {
    return MODULE_IDS.reduce(function (modules, moduleId) {
      modules[moduleId] = false;
      return modules;
    }, {});
  }

  function defaults() {
    return {
      version: POLICY_VERSION,
      modules: emptyModules()
    };
  }

  function parse(value) {
    if (typeof value !== 'string') return value;
    try {
      return JSON.parse(value);
    } catch (error) {
      return null;
    }
  }

  function isRecord(value) {
    return !!value && typeof value === 'object' && !Array.isArray(value);
  }

  function isKnownModule(moduleId) {
    return MODULE_IDS.indexOf(String(moduleId || '')) >= 0;
  }

  function normalize(value) {
    var source = parse(value);
    var policy = defaults();

    /* A legacy global boolean/string and an unknown future policy version must
       never silently turn module entry protection back on. */
    if (!isRecord(source) ||
        source.version !== POLICY_VERSION ||
        !isRecord(source.modules)) {
      return policy;
    }

    MODULE_IDS.forEach(function (moduleId) {
      policy.modules[moduleId] = source.modules[moduleId] === true;
    });
    return policy;
  }

  function setModuleEnabled(value, moduleId, enabled) {
    var policy = normalize(value);
    var id = String(moduleId || '');
    if (isKnownModule(id)) policy.modules[id] = enabled === true;
    return policy;
  }

  function serialize(value) {
    return JSON.stringify(normalize(value));
  }

  function requiresPin(value, moduleId, hasOwnerPin) {
    var id = String(moduleId || '');
    if (hasOwnerPin !== true || !isKnownModule(id)) return false;
    return normalize(value).modules[id] === true;
  }

  return {
    POLICY_VERSION: POLICY_VERSION,
    MODULE_IDS: MODULE_IDS,
    defaults: defaults,
    normalize: normalize,
    isKnownModule: isKnownModule,
    setModuleEnabled: setModuleEnabled,
    serialize: serialize,
    requiresPin: requiresPin
  };
});
