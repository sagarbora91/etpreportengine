/* Single source of truth for the Android package and in-app build identity. */
(function (root, factory) {
  var identity = factory();
  if (typeof module === 'object' && module.exports) module.exports = identity;
  if (root) root.SaagarBuildIdentity = identity;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  return Object.freeze({
    packageId: 'com.saagartraders.bcc',
    appVersion: 'V6',
    versionName: '6',
    versionCode: 600,
    minSdk: 23
  });
});
