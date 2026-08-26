(function (root) {
  'use strict';
  if (root.SaagarShellResponsive) return;

  var doc = root.document;
  var preference = 'auto';
  var listener = null;
  var listening = false;

  function width() {
    var value = Number(root.innerWidth || (doc && doc.documentElement && doc.documentElement.clientWidth) || 0);
    return value > 0 ? value : 360;
  }
  function classifyWidth(value) {
    value = Number(value) || 0;
    if (value < 640) return 'mobile';
    if (value < 900) return 'compact';
    if (value < 1200) return 'tablet';
    return 'desktop';
  }
  function normalize(value) {
    return value === 'mobile' || value === 'desktop' || value === 'auto' ? value : 'auto';
  }
  function resolvedMode(value, physicalWidth) {
    value = normalize(value);
    if (value !== 'auto') return value;
    return Number(physicalWidth) < 900 ? 'mobile' : 'desktop';
  }
  function apply() {
    var html = doc && doc.documentElement;
    var physicalWidth = width();
    var tier = classifyWidth(physicalWidth);
    var mode = resolvedMode(preference, physicalWidth);
    if (html) {
      html.setAttribute('data-shell-tier', tier);
      html.setAttribute('data-shell-ui-preference', preference);
      html.setAttribute('data-shell-ui-mode', mode);
      if (html.classList) html.classList.toggle('bcc-mobile', mode === 'mobile');
      else html.className = String(html.className || '').replace(/\bbcc-mobile\b/g, '') + (mode === 'mobile' ? ' bcc-mobile' : '');
    }
    if (typeof listener === 'function') listener(Object.freeze({ preference: preference, mode: mode, tier: tier, width: physicalWidth }));
    return mode;
  }
  function setPreference(value) {
    preference = normalize(value);
    return apply();
  }
  function refresh() { return apply(); }
  function onChange(fn) { listener = typeof fn === 'function' ? fn : null; }
  function start() {
    if (listening) return apply();
    listening = true;
    if (root.addEventListener) root.addEventListener('resize', refresh, false);
    else if (root.attachEvent) root.attachEvent('onresize', refresh);
    return apply();
  }
  function stop() {
    if (!listening) return;
    listening = false;
    if (root.removeEventListener) root.removeEventListener('resize', refresh, false);
    else if (root.detachEvent) root.detachEvent('onresize', refresh);
  }

  var api = Object.freeze({
    version: 1,
    classifyWidth: classifyWidth,
    resolvedMode: resolvedMode,
    setPreference: setPreference,
    getPreference: function () { return preference; },
    getMode: function () { return resolvedMode(preference, width()); },
    refresh: refresh,
    onChange: onChange,
    start: start,
    stop: stop
  });
  Object.defineProperty(root, 'SaagarShellResponsive', { value: api, enumerable: true, writable: false, configurable: false });
})(window);
