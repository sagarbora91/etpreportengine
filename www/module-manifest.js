(function (root, factory) {
  'use strict';
  if (typeof module === 'object' && module.exports) {
    module.exports = factory();
    return;
  }
  try {
    var api = factory();
    Object.defineProperty(root, 'SaagarModuleManifest', {
      value: api,
      enumerable: true,
      writable: false,
      configurable: false
    });
  } catch (error) {
    try {
      Object.defineProperty(root, 'SaagarModuleManifestError', {
        value: 'INVALID_MANIFEST',
        enumerable: false,
        writable: false,
        configurable: false
      });
    } catch (_) {}
    try { console.error('Saagar module manifest rejected:', error); } catch (_) {}
  }
})(typeof window !== 'undefined' ? window : globalThis, function () {
  'use strict';

  var EXPECTED_IDS = Object.freeze([
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
  var REQUIRED_FIELDS = Object.freeze([
    'id', 'title', 'short', 'category', 'icon', 'priority', 'file',
    'subtitle', 'summary', 'bytes', 'sha256', 'src'
  ]);
  var OPTIONAL_FIELDS = Object.freeze(['source_title']);
  var ALLOWED_FIELDS = Object.freeze(REQUIRED_FIELDS.concat(OPTIONAL_FIELDS));
  var SHARED_REQUIRED_FIELDS = Object.freeze(['id', 'version', 'file', 'bytes', 'sha256']);
  var BLOCKED_KEYS = Object.freeze(['__proto__', 'prototype', 'constructor']);
  var HEX_64 = /^[a-f0-9]{64}$/;
  var MODULE_ID = /^[a-z][a-z0-9_]{1,31}$/;
  var CONTROL_CHARACTER = /[\u0000-\u001f\u007f-\u009f]/;

  function fail(message) {
    throw new Error('Invalid Saagar module manifest: ' + message);
  }

  function isPlainObject(value) {
    if (!value || Object.prototype.toString.call(value) !== '[object Object]') return false;
    var prototype = Object.getPrototypeOf(value);
    return prototype === Object.prototype || prototype === null;
  }

  function ownKeys(value) {
    return Object.getOwnPropertyNames(value);
  }

  function assertObject(value, label) {
    if (!isPlainObject(value)) fail(label + ' must be a plain object');
    ownKeys(value).forEach(function (key) {
      if (BLOCKED_KEYS.indexOf(key) >= 0) fail(label + ' contains blocked key ' + key);
    });
  }

  function assertExactKeys(value, required, allowed, label) {
    var keys = ownKeys(value);
    required.forEach(function (key) {
      if (!Object.prototype.hasOwnProperty.call(value, key)) fail(label + ' is missing ' + key);
    });
    keys.forEach(function (key) {
      if (allowed.indexOf(key) < 0) fail(label + ' contains unknown field ' + key);
    });
  }

  function cleanString(value, label) {
    if (typeof value !== 'string' || !value.length) fail(label + ' must be a non-empty string');
    if (value !== value.trim()) fail(label + ' must not have surrounding whitespace');
    if (CONTROL_CHARACTER.test(value)) fail(label + ' contains control characters');
    if (typeof value.normalize === 'function' && value !== value.normalize('NFC')) {
      fail(label + ' must use NFC Unicode');
    }
    return value;
  }

  function freezeModule(value, index, seen) {
    var label = 'modules[' + index + ']';
    assertObject(value, label);
    assertExactKeys(value, REQUIRED_FIELDS, ALLOWED_FIELDS, label);

    var id = cleanString(value.id, label + '.id');
    if (!MODULE_ID.test(id)) fail(label + '.id is not canonical');
    if (id !== EXPECTED_IDS[index]) fail(label + '.id must be ' + EXPECTED_IDS[index]);

    var expectedPath = 'modules/' + id + '/index.html';
    var file = cleanString(value.file, label + '.file');
    var src = cleanString(value.src, label + '.src');
    if (file !== expectedPath || src !== expectedPath || file !== src) {
      fail(label + ' must use the canonical local module path');
    }
    if (/^(?:[a-z]+:|\/\/)|[\\?#%]|(?:^|\/)\.\.?\//i.test(src)) {
      fail(label + '.src must be a relative local POSIX path');
    }
    if (!Number.isSafeInteger(value.bytes) || value.bytes <= 0) {
      fail(label + '.bytes must be a positive safe integer');
    }
    if (typeof value.sha256 !== 'string' || !HEX_64.test(value.sha256)) {
      fail(label + '.sha256 must be lowercase SHA-256 hex');
    }

    var title = cleanString(value.title, label + '.title');
    var short = cleanString(value.short, label + '.short');
    var category = cleanString(value.category, label + '.category');
    var icon = cleanString(value.icon, label + '.icon');
    var priority = cleanString(value.priority, label + '.priority');
    var subtitle = cleanString(value.subtitle, label + '.subtitle');
    var summary = cleanString(value.summary, label + '.summary');
    var sourceTitle;
    if (Object.prototype.hasOwnProperty.call(value, 'source_title')) {
      sourceTitle = cleanString(value.source_title, label + '.source_title');
    }

    [['id', id], ['path', src], ['title', title], ['short', short], ['sha256', value.sha256]]
      .forEach(function (entry) {
        var kind = entry[0];
        var candidate = entry[1];
        if (seen[kind][candidate]) fail('duplicate ' + kind + ' ' + candidate);
        seen[kind][candidate] = true;
      });

    var module = {
      id: id,
      title: title,
      short: short,
      category: category,
      icon: icon,
      priority: priority,
      file: file,
      subtitle: subtitle,
      summary: summary,
      bytes: value.bytes,
      sha256: value.sha256
    };
    if (sourceTitle !== undefined) module.source_title = sourceTitle;
    module.src = src;
    return Object.freeze(module);
  }

  function validate(input) {
    assertObject(input, 'manifest');
    assertExactKeys(input, ['schemaVersion', 'sharedAssets', 'modules'], ['schemaVersion', 'sharedAssets', 'modules'], 'manifest');
    if (input.schemaVersion !== 2) fail('schemaVersion must be 2');
    if (!Array.isArray(input.sharedAssets) || input.sharedAssets.length !== 66) fail('sharedAssets must contain exactly sixty-six entries');
    if (!Array.isArray(input.modules)) fail('modules must be an array');
    if (input.modules.length !== EXPECTED_IDS.length) {
      fail('modules must contain exactly ' + EXPECTED_IDS.length + ' entries');
    }
    var seen = {
      id: Object.create(null),
      path: Object.create(null),
      title: Object.create(null),
      short: Object.create(null),
      sha256: Object.create(null)
    };
    var modules = input.modules.map(function (module, index) {
      return freezeModule(module, index, seen);
    });
    var expectedShared = [
      { id: 'module-bridge', file: 'shared/module-bridge.js' },
      { id: 'module-runtime', file: 'shared/module-runtime.js' },
      { id: 'mah4-runtime', file: 'shared/mah4-runtime.js' },
      { id: 'module-uniform-css', file: 'shared/module-uniform.css' },
      { id: 'module-back-css', file: 'shared/module-back.css' },
      { id: 'module-employee-css', file: 'shared/module-employee.css' }
      ,{ id: 'module-mobile-common-css', file: 'shared/module-mobile-common.css' }
      ,{ id: 'module-brand-tokens-css', file: 'shared/module-brand-tokens.css' }
      ,{ id: 'module-responsive-css', file: 'shared/module-responsive.css' }
      ,{ id: 'module-ui-runtime', file: 'shared/module-ui-runtime.js' }
      ,{ id: 'module-table-css', file: 'shared/module-table.css' }
      ,{ id: 'module-table-runtime', file: 'shared/module-table-runtime.js' }
      ,{ id: 'module-components-css', file: 'shared/module-components.css' }
      ,{ id: 'module-rendered-components', file: 'shared/module-rendered-components.js' }
      ,{ id: 'stock-ui-css', file: 'modules/stock/stock-ui.css' }
      ,{ id: 'payroll-ui-css', file: 'modules/payroll/payroll-ui.css' }
      ,{ id: 'grooming-ui-css', file: 'modules/grooming/grooming-ui.css' }
      ,{ id: 'service-ui-css', file: 'modules/service/service-ui.css' }
      ,{ id: 'leave-ui-css', file: 'modules/leave/leave-ui.css' }
      ,{ id: 'cro-audit-ui-css', file: 'modules/cro_audit/cro-audit-ui.css' }
      ,{ id: 'tax-ui-css', file: 'modules/tax/tax-ui.css' }
      ,{ id: 'dsr-ui-css', file: 'modules/dsr/dsr-ui.css' }
      ,{ id: 'qms-view', file: 'modules/qms/qms-view.js' }
      ,{ id: 'qms-ui-css', file: 'modules/qms/qms-ui.css' }
      ,{ id: 'module-delete-cell-css', file: 'shared/module-delete-cell.css' }
      ,{ id: 'etp-verified-presentation', file: 'etp-verified-presentation.js' }
      ,{ id: 'etp-verified-analytics', file: 'etp-verified-analytics.js' }
      ,{ id: 'etp-analytics-consumer', file: 'etp-analytics-consumer.js' }
      ,{ id: 'etp-operational-foundation', file: 'etp-operational-foundation.js' }
      ,{ id: 'etp-operational-store', file: 'etp-operational-store.js' }
      ,{ id: 'etp-operational-adapters', file: 'etp-operational-adapters.js' }
      ,{ id: 'etp-operational-runtime', file: 'etp-operational-runtime.js' }
      ,{ id: 'etp-e4-authority-intake', file: 'etp-e4-authority-intake.js' }
      ,{ id: 'etp-e6-authority-intake', file: 'etp-e6-authority-intake.js' }
      ,{ id: 'etp-e5-authority-intake', file: 'etp-e5-authority-intake.js' }
      ,{ id: 'etp-e7-authority-intake', file: 'etp-e7-authority-intake.js' }
      ,{ id: 'etp-e7-service-verifier', file: 'etp-e7-service-verifier.js' }
      ,{ id: 'etp-e7-service-operational', file: 'etp-e7-service-operational.js' }
      ,{ id: 'etp-cro-reconciliation', file: 'etp-cro-reconciliation.js' }
      ,{ id: 'etp-e3-orchestrator', file: 'etp-e3-orchestrator.js' }
      ,{ id: 'etp-e3-presentation', file: 'etp-e3-presentation.js' }
      ,{ id: 'etp-e3-presentation-css', file: 'etp-e3-presentation.css' }
      ,{ id: 'etp-target-planning', file: 'etp-target-planning.js' }
      ,{ id: 'etp-e4-orchestrator', file: 'etp-e4-orchestrator.js' }
      ,{ id: 'etp-e4-presentation', file: 'etp-e4-presentation.js' }
      ,{ id: 'etp-e4-presentation-css', file: 'etp-e4-presentation.css' }
      ,{ id: 'etp-e6-presentation', file: 'etp-e6-presentation.js' }
      ,{ id: 'etp-e6-presentation-css', file: 'etp-e6-presentation.css' }
      ,{ id: 'etp-e5-presentation', file: 'etp-e5-presentation.js' }
      ,{ id: 'etp-e5-presentation-css', file: 'etp-e5-presentation.css' }
      ,{ id: 'etp-operational-i18n', file: 'etp-operational-i18n.js' }
      ,{ id: 'etp-operational-i18n-css', file: 'etp-operational-i18n.css' }
      ,{ id: 'etp-e5-payroll-bridge', file: 'etp-e5-payroll-bridge.js' }
      ,{ id: 'etp-e7-presentation', file: 'etp-e7-presentation.js' }
      ,{ id: 'etp-e7-presentation-css', file: 'etp-e7-presentation.css' }
      ,{ id: 'etp-e7-module-host', file: 'etp-e7-module-host.js' }
      ,{ id: 'etp-operational-gateway', file: 'etp-operational-gateway.js' }
      ,{ id: 'etp-operational-mount', file: 'etp-operational-mount.js' }
      ,{ id: 'etp-e3-verified-join', file: 'etp-e3-verified-join.js' }
      ,{ id: 'etp-operational-bootstrap', file: 'etp-operational-bootstrap.js' }
      ,{ id: 'etp-operational-shell-composer', file: 'etp-operational-shell-composer.js' }
      ,{ id: 'etp-operational-module-host', file: 'etp-operational-module-host.js' }
      ,{ id: 'etp-operational-frame-bridge', file: 'etp-operational-frame-bridge.js' }
      ,{ id: 'etp-exception-monitor', file: 'etp-exception-monitor.js' }
      ,{ id: 'etp-incentive-control', file: 'etp-incentive-control.js' }
      ,{ id: 'etp-operations-consumer', file: 'etp-operations-consumer.js' }
    ];
    var frozenShared = input.sharedAssets.map(function(shared, index){
      var label='sharedAssets['+index+']', expected=expectedShared[index];
      assertObject(shared,label); assertExactKeys(shared,SHARED_REQUIRED_FIELDS,SHARED_REQUIRED_FIELDS,label);
      if(cleanString(shared.id,label+'.id')!==expected.id || shared.version!==1 || cleanString(shared.file,label+'.file')!==expected.file) fail('invalid shared runtime identity');
      if(!Number.isSafeInteger(shared.bytes)||shared.bytes<=0||typeof shared.sha256!=='string'||!HEX_64.test(shared.sha256)) fail('invalid shared runtime integrity');
      return Object.freeze({id:shared.id,version:1,file:shared.file,bytes:shared.bytes,sha256:shared.sha256});
    });
    return Object.freeze({ schemaVersion: 2, sharedAssets: Object.freeze(frozenShared), modules: Object.freeze(modules) });
  }

  var RAW_MANIFEST = /*__SAAGAR_MODULE_MANIFEST_START__*/{
  "schemaVersion": 2,
  "sharedAssets": [
    {
      "id": "module-bridge",
      "version": 1,
      "file": "shared/module-bridge.js",
      "bytes": 3084,
      "sha256": "ab237e01c10c480dd8cd49f0f9ea6cdaccf6781240292ae72549b7391cb71b9c"
    },
    {
      "id": "module-runtime",
      "version": 1,
      "file": "shared/module-runtime.js",
      "bytes": 20392,
      "sha256": "e4c907a922ff7e944da250905b8c022132609fa871683b0816bf8aeb4f6dba47"
    },
    {
      "id": "mah4-runtime",
      "version": 1,
      "file": "shared/mah4-runtime.js",
      "bytes": 10068,
      "sha256": "458410440a3748c4b8a59726e25f8eb59d61588f79fdc1f0ae3fc235aa2d48e5"
    },
    {
      "id": "module-uniform-css",
      "version": 1,
      "file": "shared/module-uniform.css",
      "bytes": 6715,
      "sha256": "b90c167b7e61f6cf6f5abd010345052b52130221feecaa17e8065405d1c67fc8"
    },
    {
      "id": "module-back-css",
      "version": 1,
      "file": "shared/module-back.css",
      "bytes": 1967,
      "sha256": "c2de8d36022ef12c7828a9902731ad602e4e34de78772174892798533203bec3"
    },
    {
      "id": "module-employee-css",
      "version": 1,
      "file": "shared/module-employee.css",
      "bytes": 325,
      "sha256": "128dec9007df4bbb1234a21ed99c29582f2a08c47ce807ab5d73f8fc85bc0fd8"
    },
    {
      "id": "module-mobile-common-css",
      "version": 1,
      "file": "shared/module-mobile-common.css",
      "bytes": 26137,
      "sha256": "b8ae67010cb906861df2e858079106a1d76a122ccc6f2748b9090a843d7f67f2"
    },
    {
      "id": "module-brand-tokens-css",
      "version": 1,
      "file": "shared/module-brand-tokens.css",
      "bytes": 1479,
      "sha256": "89e5bcff9ea1b67ea1063d8caa80123e12367e23a1f9ab575ffa234ed8162f85"
    },
    {
      "id": "module-responsive-css",
      "version": 1,
      "file": "shared/module-responsive.css",
      "bytes": 1692,
      "sha256": "050413e0a6bd9969610c904775c77166576230d05cac3ab1c1d2bc0051aad05a"
    },
    {
      "id": "module-ui-runtime",
      "version": 1,
      "file": "shared/module-ui-runtime.js",
      "bytes": 2733,
      "sha256": "d4e5a0618905bf9e1127ea0c68439a477641df34bfdc0fdf4115aa0f4c9c0f30"
    },
    {
      "id": "module-table-css",
      "version": 1,
      "file": "shared/module-table.css",
      "bytes": 4094,
      "sha256": "b3004d7d944bdaa06ecdc35d95ff4e35891ae0482a16b4dae4c69130e8091990"
    },
    {
      "id": "module-table-runtime",
      "version": 1,
      "file": "shared/module-table-runtime.js",
      "bytes": 4827,
      "sha256": "cb8f46ab93b299469bf894179c1482980c59adb4634864d81510148000596e96"
    },
    {
      "id": "module-components-css",
      "version": 1,
      "file": "shared/module-components.css",
      "bytes": 5923,
      "sha256": "c2e64460d16f6852d82cd3ac74bb597c31c88ae1ac3f3b02fe58044c5ea089b6"
    },
    {
      "id": "module-rendered-components",
      "version": 1,
      "file": "shared/module-rendered-components.js",
      "bytes": 11387,
      "sha256": "22e393c60d0e656cf1aade9cfaf8108944aa2aedc87c074ebf9f9be5f07649ab"
    },
    {
      "id": "stock-ui-css",
      "version": 1,
      "file": "modules/stock/stock-ui.css",
      "bytes": 32833,
      "sha256": "0c661d393cef898f5e3d30bb55f21d575e52ec34640ffecea80a5f43b5828a4a"
    },
    {
      "id": "payroll-ui-css",
      "version": 1,
      "file": "modules/payroll/payroll-ui.css",
      "bytes": 50046,
      "sha256": "771aef10af6c75e176c1be930555992d99d6251ba9663062620f47b031f462c2"
    },
    {
      "id": "grooming-ui-css",
      "version": 1,
      "file": "modules/grooming/grooming-ui.css",
      "bytes": 20736,
      "sha256": "65a211ec63c7f383f18414a2edd145dfae98d13d3ae1b8540f226ea3df8b8201"
    },
    {
      "id": "service-ui-css",
      "version": 1,
      "file": "modules/service/service-ui.css",
      "bytes": 47207,
      "sha256": "3204554d562a4ef78df74dd4ab02dc288247acd6e8da0db8406c69cead8832dc"
    },
    {
      "id": "leave-ui-css",
      "version": 1,
      "file": "modules/leave/leave-ui.css",
      "bytes": 33346,
      "sha256": "bcf60690b54f04464195c8a5ab5229bc24860a97e711f4b407cbfa11b4f52828"
    },
    {
      "id": "cro-audit-ui-css",
      "version": 1,
      "file": "modules/cro_audit/cro-audit-ui.css",
      "bytes": 30980,
      "sha256": "fee92fd1f3c3f0cfc4c0ef13f7ced5772fa305a6a61b383e45a33600b8c8b42a"
    },
    {
      "id": "tax-ui-css",
      "version": 1,
      "file": "modules/tax/tax-ui.css",
      "bytes": 43128,
      "sha256": "acd9a5fa82f71c7e864fc27333f3f668aef65b7cb83a0c8f4ac55c71cdf9a352"
    },
    {
      "id": "dsr-ui-css",
      "version": 1,
      "file": "modules/dsr/dsr-ui.css",
      "bytes": 47602,
      "sha256": "701dbfcad1c431bbaf60b9159d7fc85433bd1825b048149db37e83b39574b747"
    },
    {
      "id": "qms-view",
      "version": 1,
      "file": "modules/qms/qms-view.js",
      "bytes": 4711,
      "sha256": "ef3f136937c3674980996b3da6fb67b6f27d008c0be7627c4e7c490185d25a6b"
    },
    {
      "id": "qms-ui-css",
      "version": 1,
      "file": "modules/qms/qms-ui.css",
      "bytes": 24920,
      "sha256": "d775c662072958c3697a7b2a36ec45fd27cfb011cb5dae8dc0d0edc817ac73c4"
    },
    {
      "id": "module-delete-cell-css",
      "version": 1,
      "file": "shared/module-delete-cell.css",
      "bytes": 311,
      "sha256": "240ace998628aa92a455791534ed300d8bde3af557405468a4916f980e0ade5b"
    },
    {
      "id": "etp-verified-presentation",
      "version": 1,
      "file": "etp-verified-presentation.js",
      "bytes": 24310,
      "sha256": "d7ad933716c4a85b2b4ac5f94f0c4aa5cc769dca3e4c0b2e2d154ca344572b5d"
    },
    {
      "id": "etp-verified-analytics",
      "version": 1,
      "file": "etp-verified-analytics.js",
      "bytes": 11412,
      "sha256": "5fb9eda540b062a4209c00186f9dbbe91637ad8f47b35a0846686a48c2eca1ce"
    },
    {
      "id": "etp-analytics-consumer",
      "version": 1,
      "file": "etp-analytics-consumer.js",
      "bytes": 4850,
      "sha256": "a069996397f4720aeeb392544f35232939039dde6e69f61572df9a3223ef48a0"
    },
    {
      "id": "etp-operational-foundation",
      "version": 1,
      "file": "etp-operational-foundation.js",
      "bytes": 8927,
      "sha256": "cc03dbfc31454b5dfa3b8ccef554c14909bdb20db0b668552d752e07ba775c68"
    },
    {
      "id": "etp-operational-store",
      "version": 1,
      "file": "etp-operational-store.js",
      "bytes": 12228,
      "sha256": "1628d9ce9a21318b497c9ece638775c3d6a8b565698e9aabd94883e60997edc4"
    },
    {
      "id": "etp-operational-adapters",
      "version": 1,
      "file": "etp-operational-adapters.js",
      "bytes": 13396,
      "sha256": "b04d7ea269f4ae8e49d8e61bfcf57cc658a4995717bf3314c4c9979225435d02"
    },
    {
      "id": "etp-operational-runtime",
      "version": 1,
      "file": "etp-operational-runtime.js",
      "bytes": 6449,
      "sha256": "75352426907335e7aba9da89267f2cb28e106b2efbc2eab76042d6a8c4f0e8e1"
    },
    {
      "id": "etp-e4-authority-intake",
      "version": 1,
      "file": "etp-e4-authority-intake.js",
      "bytes": 11007,
      "sha256": "b63238a928804ee967251161548317dc7445c51f6d8970983080a5544331fbd9"
    },
    {
      "id": "etp-e6-authority-intake",
      "version": 1,
      "file": "etp-e6-authority-intake.js",
      "bytes": 8054,
      "sha256": "2d233055212ff5772ee05f436a63d656f5c708035aba59baf757c4ec99048a9d"
    },
    {
      "id": "etp-e5-authority-intake",
      "version": 1,
      "file": "etp-e5-authority-intake.js",
      "bytes": 11051,
      "sha256": "ebef537bcd465c702950170074603e143e48442689bd2dbf555b6e3826f556b3"
    },
    {
      "id": "etp-e7-authority-intake",
      "version": 1,
      "file": "etp-e7-authority-intake.js",
      "bytes": 10955,
      "sha256": "dbd0c845c6b5ca90e108e6ffdd061847fe6720a81a7940747028dd5bbdcf1977"
    },
    {
      "id": "etp-e7-service-verifier",
      "version": 1,
      "file": "etp-e7-service-verifier.js",
      "bytes": 9009,
      "sha256": "1a5d2e94b0a0e36fac4a8b856fcc44122a44f8481d6f49742a529d0cd807f13f"
    },
    {
      "id": "etp-e7-service-operational",
      "version": 1,
      "file": "etp-e7-service-operational.js",
      "bytes": 22922,
      "sha256": "4de2f124d25286187e07ec661cfd972bd975f1a1684c23c8326a354bc181d1bc"
    },
    {
      "id": "etp-cro-reconciliation",
      "version": 1,
      "file": "etp-cro-reconciliation.js",
      "bytes": 13492,
      "sha256": "da659932f4674b0c5392ed0b55bf1523e406c46ca307261b7f6ee491d17eb33c"
    },
    {
      "id": "etp-e3-orchestrator",
      "version": 1,
      "file": "etp-e3-orchestrator.js",
      "bytes": 9899,
      "sha256": "415ac766b9bbc85dea12f37fd1ec9e200415dbf0c9b3b4fd281fb5779a3173c5"
    },
    {
      "id": "etp-e3-presentation",
      "version": 1,
      "file": "etp-e3-presentation.js",
      "bytes": 12671,
      "sha256": "a72eaf8a85719814d3965e9901099f26aea7f19ef8d1ce8a955bf5b2de4974bd"
    },
    {
      "id": "etp-e3-presentation-css",
      "version": 1,
      "file": "etp-e3-presentation.css",
      "bytes": 1758,
      "sha256": "2f6cb2e0c23ba7d8f50b34f6610e3c6a026ff6d6bc6cddea23860f8bf7d76503"
    },
    {
      "id": "etp-target-planning",
      "version": 1,
      "file": "etp-target-planning.js",
      "bytes": 15940,
      "sha256": "fceb678978631812a096469750b371516de0364795777b963b6ffb3166ae667d"
    },
    {
      "id": "etp-e4-orchestrator",
      "version": 1,
      "file": "etp-e4-orchestrator.js",
      "bytes": 9942,
      "sha256": "fc8e69eaf9f7c9b6a131d777786e67dc5ecc234040b383229c5ac615653a900a"
    },
    {
      "id": "etp-e4-presentation",
      "version": 1,
      "file": "etp-e4-presentation.js",
      "bytes": 14689,
      "sha256": "d90c6157cea71eb19e3414c2c656a429a10c7bc0d2f07f957fd03b55e431e413"
    },
    {
      "id": "etp-e4-presentation-css",
      "version": 1,
      "file": "etp-e4-presentation.css",
      "bytes": 2061,
      "sha256": "a1296651c8630d80f241b6f854fff91bb7e89db576ea89a48977f9f5694f0c2c"
    },
    {
      "id": "etp-e6-presentation",
      "version": 1,
      "file": "etp-e6-presentation.js",
      "bytes": 16561,
      "sha256": "b4f833974dd260a3c3290323125044e44e1cee105e6047c1074515715c068e85"
    },
    {
      "id": "etp-e6-presentation-css",
      "version": 1,
      "file": "etp-e6-presentation.css",
      "bytes": 2657,
      "sha256": "e68f5ce41916aa5d834eacab3e373fd7b25139e62bd73dc9375b3f50c015ed9d"
    },
    {
      "id": "etp-e5-presentation",
      "version": 1,
      "file": "etp-e5-presentation.js",
      "bytes": 16492,
      "sha256": "6af578d5d4b69073988f1088ce208595e989b04801076ec2495b172b8c3368aa"
    },
    {
      "id": "etp-e5-presentation-css",
      "version": 1,
      "file": "etp-e5-presentation.css",
      "bytes": 2214,
      "sha256": "193b73ac3d03f5ffaa719222154dd908082a5c0d2f7c4fb4e6beea4ac9d19823"
    },
    {
      "id": "etp-operational-i18n",
      "version": 1,
      "file": "etp-operational-i18n.js",
      "bytes": 16193,
      "sha256": "1c6801a39186621079db3468e1b0e612b161e72abeb7160e8d55e1c0a2a184c4"
    },
    {
      "id": "etp-operational-i18n-css",
      "version": 1,
      "file": "etp-operational-i18n.css",
      "bytes": 1029,
      "sha256": "679de8d2d7af6088131f9440d0b17542f6d179fb747d2272d8ad87f47318cc5f"
    },
    {
      "id": "etp-e5-payroll-bridge",
      "version": 1,
      "file": "etp-e5-payroll-bridge.js",
      "bytes": 2846,
      "sha256": "c1bf788f9cc41a11bffd9fe07637cc726856e72d93f3bc2de12fe90d4e4d1e83"
    },
    {
      "id": "etp-e7-presentation",
      "version": 1,
      "file": "etp-e7-presentation.js",
      "bytes": 16782,
      "sha256": "a40d1f3ffa7ce63bc69ba2f88d2bc441e8fd0fd407087c469de3e55a00720f69"
    },
    {
      "id": "etp-e7-presentation-css",
      "version": 1,
      "file": "etp-e7-presentation.css",
      "bytes": 2033,
      "sha256": "4a1bcbf5e8a4f1980960da91803b7fdc0b23fd4d97c2eef9cdd3c6cffa1db1a3"
    },
    {
      "id": "etp-e7-module-host",
      "version": 1,
      "file": "etp-e7-module-host.js",
      "bytes": 2672,
      "sha256": "e9ab91e040377c7bdff5b08293c0ddaddf372dc64c7e733898a01b92094ec247"
    },
    {
      "id": "etp-operational-gateway",
      "version": 1,
      "file": "etp-operational-gateway.js",
      "bytes": 22320,
      "sha256": "fdf683be38447db30b54d1fa84ce5e05e735ada0e559c24d7f97ceb33fda3e8d"
    },
    {
      "id": "etp-operational-mount",
      "version": 1,
      "file": "etp-operational-mount.js",
      "bytes": 7704,
      "sha256": "4009fd78a5964d09d4fb21db79c4cb4d9319de3e95df99f9ea802251d91e5763"
    },
    {
      "id": "etp-e3-verified-join",
      "version": 1,
      "file": "etp-e3-verified-join.js",
      "bytes": 5396,
      "sha256": "e81e9669e8ad74978531fe861c1259c1174217f67ad231692c33899fd4747c5a"
    },
    {
      "id": "etp-operational-bootstrap",
      "version": 1,
      "file": "etp-operational-bootstrap.js",
      "bytes": 18717,
      "sha256": "896bcd4279ba0a9d4127f051924c018548aff28a4ce9f41f10b767bf757b1389"
    },
    {
      "id": "etp-operational-shell-composer",
      "version": 1,
      "file": "etp-operational-shell-composer.js",
      "bytes": 7999,
      "sha256": "b42469d1ea1bed0a017896994473565e814af99aa63576db36ec013f4c5991da"
    },
    {
      "id": "etp-operational-module-host",
      "version": 1,
      "file": "etp-operational-module-host.js",
      "bytes": 6515,
      "sha256": "2ea4123c3e593e3625cfc3edb4878cd3f812cb979eccda3cfb80796e90741f98"
    },
    {
      "id": "etp-operational-frame-bridge",
      "version": 1,
      "file": "etp-operational-frame-bridge.js",
      "bytes": 869,
      "sha256": "9d873fac0f72818eb5104cad07ffbd00258cd61ebce3bf02f75421c96734c05d"
    },
    {
      "id": "etp-exception-monitor",
      "version": 1,
      "file": "etp-exception-monitor.js",
      "bytes": 24312,
      "sha256": "2e32a0a3a1491c95ddd2e474118e27b3adeb35d952f89dc70b3dbcfaeadf76a0"
    },
    {
      "id": "etp-incentive-control",
      "version": 1,
      "file": "etp-incentive-control.js",
      "bytes": 15264,
      "sha256": "fefb9bc2537ca9f7cddb48692e0bfb80b055ea0d2dcbf9a1cd688f479b3c9898"
    },
    {
      "id": "etp-operations-consumer",
      "version": 1,
      "file": "etp-operations-consumer.js",
      "bytes": 1633,
      "sha256": "59cef49ac2ce4b4836d9fdc49b9b8412da1da5429f0227da21d6ecbda33993e9"
    }
  ],
  "modules": [
    {
      "id": "stock",
      "title": "Stock Register",
      "short": "Stock",
      "category": "Operations",
      "icon": "📦",
      "priority": "High control",
      "file": "modules/stock/index.html",
      "subtitle": "Daily opening, inward, sale, transfer, return, physical and closing stock control.",
      "summary": "Inventory movement and variance control for store operations.",
      "bytes": 167716,
      "sha256": "548d1f4f5e1b1b4b9b04f14ec73c73537fb7ca470094055afb34379ff229392d",
      "source_title": "Saagar Traders — Daily Stock Register v3",
      "src": "modules/stock/index.html"
    },
    {
      "id": "service",
      "title": "Watch Service Centre",
      "short": "Service",
      "category": "Operations",
      "icon": "⌚",
      "priority": "Live tracking",
      "file": "modules/service/index.html",
      "subtitle": "Job cards, repair stages, customer tracking, delivery and billing support.",
      "summary": "End-to-end watch repair and service-centre workflow.",
      "bytes": 192878,
      "sha256": "492689a8d982e3bfc648cb12e9cb60c189f2034675bf3c11742bdef3d199c56c",
      "source_title": "Watch Service Centre — Saagar Traders",
      "src": "modules/service/index.html"
    },
    {
      "id": "qms",
      "title": "Queue Management",
      "short": "Queue",
      "category": "Operations",
      "icon": "🎯",
      "priority": "Live floor",
      "file": "modules/qms/index.html",
      "subtitle": "Walk-in capture, CRO rotation, lead closure (sale / service / non-purchase) and follow-ups.",
      "summary": "Front-desk queue + CRO rotation. Closures auto-fill the Daily Staff Register.",
      "bytes": 149652,
      "sha256": "927790da02217cc920c87573cd1cd7494736342655949acd2e55b8bc2e6eb6aa",
      "src": "modules/qms/index.html"
    },
    {
      "id": "dsr",
      "title": "CRO Login",
      "short": "CRO Login",
      "category": "Operations",
      "icon": "📋",
      "priority": "Daily accountability",
      "file": "modules/dsr/index.html",
      "subtitle": "Per-CRO daily log: opening, in/out, sales, non-purchase, tasks, marketing, cleaning (photo), closing, SM audit.",
      "summary": "CRO daily accountability hub; receives QMS auto-fill; rolls counts up to Stock.",
      "bytes": 142125,
      "sha256": "f692513e0ae4ca2c23f3d81d1c255e8a292a74b2d67280e0c0b93421f767e9f9",
      "src": "modules/dsr/index.html"
    },
    {
      "id": "expense",
      "title": "Expense Manager — Central Ledger",
      "short": "Expense",
      "category": "Finance",
      "icon": "₹",
      "priority": "Approval safe",
      "file": "modules/expense/index.html",
      "subtitle": "Central financial ledger: income/expense, auto-locked daily cash statement, cross-module feeds, maker-checker.",
      "summary": "Single master ledger with auto cash reconciliation and WSC/Payroll/Stock/QMS integration.",
      "bytes": 168863,
      "sha256": "d668b8381edf654c54467ac39e1832de2e5b440186d037a9efbe981b2419b99c",
      "source_title": "Tanishq Gold Mart · Expense Manager",
      "src": "modules/expense/index.html"
    },
    {
      "id": "grooming",
      "title": "Grooming Checklist",
      "short": "Grooming",
      "category": "Staff",
      "icon": "✅",
      "priority": "Daily discipline",
      "file": "modules/grooming/index.html",
      "subtitle": "Daily staff presentation checklist, scoring and monthly records.",
      "summary": "Readiness and staff grooming compliance tracker.",
      "bytes": 64778,
      "sha256": "6ae521a672e18bc85fb89649b7928b2bb94b6c3a380e4cb9ffe651d2cdd4d396",
      "source_title": "Saagar Traders — Grooming Checklist",
      "src": "modules/grooming/index.html"
    },
    {
      "id": "cro_audit",
      "title": "Store Manager",
      "short": "Store Manager",
      "category": "Staff",
      "icon": "🎖️",
      "priority": "Daily rubric",
      "file": "modules/cro_audit/index.html",
      "subtitle": "10-task daily CRO performance rubric with store/CRO/SM selectors, dashboard trends and targets.",
      "summary": "Daily 10-point CRO scoring; pulls grooming score; trend dashboard.",
      "bytes": 109254,
      "sha256": "e2ee0550b4019b43af08ecce96d9fd32a5c820b8268b5889944904ceb2749db2",
      "src": "modules/cro_audit/index.html"
    },
    {
      "id": "payroll",
      "title": "Saagar Traders — Payroll",
      "short": "Payroll",
      "category": "Staff",
      "icon": "💰",
      "priority": "Payroll safe",
      "file": "modules/payroll/index.html",
      "subtitle": "Saagar Traders Payroll Suite — attendance, salary days, deductions, statutory, PDF/Excel payslips. Data key unchanged.",
      "summary": "Latest Saagar Traders payroll (single-file, offline). Same payroll_suite_v1_2026 data as before.",
      "bytes": 219484,
      "sha256": "4f2fe1c64fc6262e510d37db15ab24f1ec0e9f57a3d9a56d1f038863a409102f",
      "source_title": "Gold Mart Group — Payroll Suite",
      "src": "modules/payroll/index.html"
    },
    {
      "id": "leave",
      "title": "Staff Leave Calendar",
      "short": "Leave",
      "category": "Planning",
      "icon": "🗓️",
      "priority": "Capacity view",
      "file": "modules/leave/index.html",
      "subtitle": "Leave planning, holiday visibility and staff availability calendar.",
      "summary": "Team leave management and availability control.",
      "bytes": 136545,
      "sha256": "2a5e6cea877d8bbbf233f72f096fd3b2d591614efb7cf90b4150f48fadd19e90",
      "source_title": "Staff Leave Manager",
      "src": "modules/leave/index.html"
    },
    {
      "id": "tax",
      "title": "Tax Compliance Calendar",
      "short": "Compliance",
      "category": "Compliance",
      "icon": "🛡️",
      "priority": "Deadline control",
      "file": "modules/tax/index.html",
      "subtitle": "GST, TDS and statutory compliance due-date operating calendar.",
      "summary": "Indian statutory deadline tracker with compliance status controls.",
      "bytes": 191063,
      "sha256": "9da8a3b4107342d99efbed44395c5372f03d7594da342115d2d27d8ac4ffa8a1",
      "source_title": "Compliance Operating System — Indian Firms v2",
      "src": "modules/tax/index.html"
    },
    {
      "id": "planning",
      "title": "Festival & Season Planner",
      "short": "Planning",
      "category": "Planning",
      "icon": "🎊",
      "priority": "Seasonal targets",
      "file": "modules/planning/index.html",
      "subtitle": "Festival targets, pre-season prep checklists and staff leave-blackout windows.",
      "summary": "Plan peak seasons — targets vs QMS actuals, prep checklists and leave-freeze dates.",
      "bytes": 14670,
      "sha256": "dad7b6707f30cb5c8d94cddab3d218779118b817d896bc2d668a77e753a6bffb",
      "src": "modules/planning/index.html"
    },
    {
      "id": "etp",
      "title": "Retail ETP Reports",
      "short": "Retail ETP",
      "category": "Reports",
      "icon": "📊",
      "priority": "Verified reporting",
      "file": "modules/etp/index.html",
      "subtitle": "Secure Retail ETP import, verified reports, coverage history and reconciliation controls.",
      "summary": "Reports-owned shell for governed R003, R013, R022 and R025 workflows.",
      "bytes": 42832,
      "sha256": "c28a721121dfa3fa6799d1eeb782fa364f54ad5189152d89748a812dddb5f12a",
      "src": "modules/etp/index.html"
    }
  ]
}/*__SAAGAR_MODULE_MANIFEST_END__*/;

  var manifest = validate(RAW_MANIFEST);
  var byId = Object.create(null);
  manifest.modules.forEach(function (module) { byId[module.id] = module; });
  Object.freeze(byId);

  return Object.freeze({
    schemaVersion: manifest.schemaVersion,
    sharedAssets: manifest.sharedAssets,
    modules: manifest.modules,
    ids: EXPECTED_IDS,
    get: function (id) { return byId[String(id)] || null; },
    getShared: function (id) { for(var i=0;i<manifest.sharedAssets.length;i++)if(manifest.sharedAssets[i].id===id)return manifest.sharedAssets[i];return null; },
    has: function (id) { return !!byId[String(id)]; },
    validate: validate
  });
});
