(function (root) {
  'use strict';
  if (root.SaagarModuleBridge) return;

  var MODULE_BRIDGE_VERSION = 1;
  var parentWindow = null;
  try { parentWindow = root.parent && root.parent !== root ? root.parent : null; } catch (_) {}

  function parentValue(name) {
    try { return parentWindow ? parentWindow[name] : undefined; } catch (_) { return undefined; }
  }
  function localOrParent(name) {
    try { if (root[name] !== undefined) return root[name]; } catch (_) {}
    return parentValue(name);
  }
  function getter(read) {
    return { enumerable: true, get: read };
  }

  var api = { version: MODULE_BRIDGE_VERSION };
  Object.defineProperties(api, {
    waConfig: getter(function () { return parentValue('WA_CFG'); }),
    adminPinCheck: getter(function () { return parentValue('SaagarAdminPinCheck'); }),
    dsrCompletionPolicy: getter(function () { return localOrParent('SaagarDsrCompletionPolicy'); }),
    ownerSession: getter(function () { return parentValue('SaagarOwnerSession'); }),
    share: getter(function () { return parentValue('SaagarShare'); }),
    shareText: getter(function () { return parentValue('shareText'); }),
    sharedStorage: getter(function () { return parentValue('localStorage'); }),
    evidence: getter(function () { return localOrParent('SaagarEvidence'); }),
    legal: getter(function () { return localOrParent('SaagarLegal'); }),
    reauth: getter(function () { return localOrParent('SaagarReauth'); }),
    report: getter(function () { return localOrParent('SaagarReport'); }),
    servicePersistence: getter(function () { return localOrParent('SaagarServicePersistence'); }),
    serviceWorkboardPolicy: getter(function () { return localOrParent('SaagarServiceWorkboardPolicy'); }),
    qmsPersistence: getter(function () { return localOrParent('SaagarQmsPersistence'); }),
    qmsPolicy: getter(function () { return localOrParent('SaagarQmsPolicy'); }),
    qmsArchiveLookup: getter(function () { return parentValue('qmsArchiveLookup'); }),
    photo: getter(function () { return localOrParent('SaagarPhoto'); }),
    jsZip: getter(function () { return parentValue('JSZip'); }),
    ensureJsZip: getter(function () { return parentValue('ensureJSZip'); }),
    etpImportGateway: getter(function () { var gateway = parentValue('SaagarEtpModuleGateway'); return gateway && gateway.importFacade; }),
    etpReadGateway: getter(function () { var demo = parentValue('SaagarEtpDemoOperational'), gateway = parentValue('SaagarEtpModuleGateway'); return demo && demo.syntheticOnly === true && demo.readGateway ? demo.readGateway : gateway && gateway.readFacade; }),
    e7ServiceVerification: getter(function () { return parentValue('SaagarEtpE7OperationalFacade'); }),
    e7ServiceActor: getter(function () { return parentValue('SaagarEtpE7ServiceActor'); }),
    getE7ServiceActionInput: getter(function () { return parentValue('SaagarEtpE7ServiceActionInput'); })
  });
  Object.freeze(api);
  Object.defineProperty(root, 'SaagarModuleBridge', {
    value: api, enumerable: true, writable: false, configurable: false
  });
})(window);
