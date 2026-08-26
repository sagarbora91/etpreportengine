import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readdir, readFile } from 'node:fs/promises';

const root = new URL('../www/', import.meta.url);
const source = await readFile(new URL('app-i18n.js', root), 'utf8').catch(() => '');
const shell = await readFile(new URL('index.html', root), 'utf8');
const moduleNames = (await readdir(new URL('modules/', root), { withFileTypes: true }))
  .filter(entry => entry.isDirectory())
  .map(entry => entry.name)
  .sort();

function loadRuntime(initialLanguage) {
  const listeners = {};
  let derivedObjectCreates = 0;
  const instrumentedObject = {
    create(prototype) { derivedObjectCreates += 1; return Object.create(prototype); },
    freeze: Object.freeze
  };
  const document = {
    readyState: 'loading',
    documentElement: { lang: 'en' },
    addEventListener(type, handler) { listeners[type] = handler; },
    querySelectorAll() { return []; }
  };
  const storage = new Map();
  const localStorage = {
    getItem(key) { return storage.has(key) ? storage.get(key) : null; },
    setItem(key, value) { storage.set(key, String(value)); }
  };
  if (initialLanguage) storage.set('saagar_lang', initialLanguage);
  const window = {
    document,
    localStorage,
    addEventListener(type, handler) { listeners[`window:${type}`] = handler; },
    setTimeout(handler) { handler(); return 1; },
    clearTimeout() {}
  };
  window.window = window;
  vm.runInNewContext(source, { window, document, localStorage, console, Object: instrumentedObject,
    MutationObserver: class { observe() {} } });
  return { api: window.SaagarI18n, document, listeners, storage,
    derivedObjectCreates: () => derivedObjectCreates };
}

function loadApi() { return loadRuntime().api; }

test('one offline localization runtime is loaded by the shell and every module', async () => {
  assert.match(shell, /<script src="app-i18n\.js"><\/script>/);
  assert.equal(moduleNames.length, 12);
  for (const moduleName of moduleNames) {
    const html = await readFile(new URL(`modules/${moduleName}/index.html`, root), 'utf8');
    assert.match(html, /<script src="\.\.\/\.\.\/app-i18n\.js"><\/script>/, `${moduleName} must load shared language runtime`);
  }
});

test('Marathi and Hindi dictionaries cover Settings, module names and common actions', () => {
  const api = loadApi();
  assert.ok(api, 'SaagarI18n must be exported');
  assert.equal(api.translate('Settings', 'mr'), 'सेटिंग्ज');
  assert.equal(api.translate('Appearance & language', 'mr'), 'दिसणे आणि भाषा');
  assert.equal(api.translate('Stock Register', 'mr'), 'स्टॉक नोंदवही');
  assert.equal(api.translate("Start Today's Register", 'mr'), 'आजची नोंदवही सुरू करा');
  assert.equal(api.translate('New Service Order', 'mr'), 'नवीन सेवा आदेश');
  assert.equal(api.translate('Opening Stock — Daily Head Count Register', 'mr'), 'आरंभीचा साठा — दैनिक मोजणी नोंदवही');
  assert.equal(api.translate('Desktop - Marathi - Normal text', 'mr'), 'डेस्कटॉप - मराठी - सामान्य मजकूर');
  assert.equal(api.translate('Owner PIN not set - 2 protected', 'mr'), 'मालक पिन सेट नाही - 2 संरक्षित');
  assert.equal(api.translate('Save', 'hi'), 'सहेजें');
  assert.equal(api.translate('Manage Compliance', 'hi'), 'अनुपालन प्रबंधन');
  assert.equal(api.translate('Controlled export is available only inside Saagar Control Centre.', 'mr'),
    'नियंत्रित निर्यात फक्त Saagar Control Centre मध्ये उपलब्ध आहे.');
  assert.equal(api.translate('Report engine unavailable on this build.', 'hi'),
    'इस बिल्ड में रिपोर्ट प्रणाली उपलब्ध नहीं है।');
  assert.equal(api.translate('Historical view is read-only', 'mr'),
    'मागील नोंदींचे दृश्य फक्त वाचनासाठी आहे');
  assert.equal(api.translate('No employees selected.', 'hi'), 'कोई कर्मचारी नहीं चुना गया है।');
  assert.equal(api.translate('Export blocked: device security status could not be verified.', 'mr'),
    'निर्यात अवरोधित: उपकरणाची सुरक्षा स्थिती पडताळता आली नाही.');
  assert.equal(api.translate('Report delivery blocked — approval token is no longer valid.', 'hi'),
    'रिपोर्ट वितरण अवरुद्ध — मंज़ूरी टोकन अब मान्य नहीं है।');
  assert.equal(api.translate('Recovery code:', 'mr'), 'पुनर्प्राप्ती कोड:');
  assert.equal(api.translate('Planning closed because its secure runtime did not complete.', 'hi'),
    'सुरक्षित रनटाइम पूरा न होने के कारण योजना मॉड्यूल बंद कर दिया गया।');
  assert.equal(api.translate('Open Manager workspace', 'mr'), 'व्यवस्थापक कार्यक्षेत्र उघडा');
  assert.equal(api.translate('Could not create PDF:', 'hi'), 'PDF नहीं बनाई जा सकी:');
  assert.equal(api.translate('Privacy contact saved', 'mr'), 'गोपनीयता संपर्क जतन केला');
  assert.equal(api.translate('Incident clock started:', 'hi'), 'घटना की समय-गणना शुरू की गई:');
  assert.equal(api.translate('Accounts / Owner only', 'mr'), 'खाती / फक्त मालकासाठी');
  assert.equal(api.translate('Select an employee (add one in Employee Master first).', 'hi'),
    'कर्मचारी चुनें (पहले कर्मचारी मास्टर में कर्मचारी जोड़ें)।');
  assert.equal(api.translate('Only SM can lock/unlock.', 'mr'),
    'फक्त स्टोअर मॅनेजर लॉक/अनलॉक करू शकतो.');
  assert.equal(api.translate('Switch Mobile / Desktop layout', 'mr'),
    'मोबाइल / डेस्कटॉप लेआउट स्विच करा');
  assert.equal(api.translate('Full name *', 'hi'), 'पूरा नाम*');
  assert.equal(api.translate('Privacy notice is unavailable — do not collect customer data.', 'mr'),
    'गोपनीयता सूचना अनुपलब्ध आहे — ग्राहक डेटा संकलित करू नका.');
  assert.equal(api.translate('Ask for the Admin PIN when the app starts', 'mr'),
    'ॲप सुरू झाल्यावर ॲडमिन पिनसाठी विचारा');
  assert.equal(api.translate('Allow owner-approved exports on this device', 'hi'),
    'इस डिवाइस पर स्वामी-अनुमोदित निर्यात की अनुमति दें');
  assert.equal(api.translate('View export register', 'mr'), 'निर्यात रजिस्टर पहा');
  assert.equal(api.translate('Unmapped business value', 'mr'), 'Unmapped business value');
  const stats = api.stats();
  assert.ok(stats.mr >= 1150, `expected >=1150 Marathi UI phrases, got ${stats.mr}`);
  assert.equal(stats.mr, stats.hi, 'Marathi and Hindi must have equal phrase coverage');
});

test('English startup defers only derived lookup construction and preserves the synchronous API', () => {
  assert.match(source, /var dictionaries=null;\s*var wordMaps=null;/);
  assert.match(source, /function ensureDerivedDictionaries\(\)[\s\S]*for\(var i=0;i<PHRASES\.length;i\+\+\)/);
  const translateSource = source.slice(source.indexOf('function translate('), source.indexOf('function blocked('));
  assert.ok(translateSource.indexOf("if(lang==='en') return text;") < translateSource.indexOf('ensureDerivedDictionaries();'));
  const runtime = loadRuntime('en');
  assert.equal(runtime.derivedObjectCreates(), 0, 'English startup must not construct derived lookup objects');
  assert.deepEqual(Object.keys(runtime.api).sort(), ['apply', 'getLanguage', 'setLanguage', 'stats', 'translate']);
  assert.equal(Object.isFrozen(runtime.api), true);
  assert.equal(runtime.api.apply('en'), 'en');
  assert.equal(runtime.api.translate('Settings', 'en'), 'Settings');
  assert.equal(runtime.api.stats().mr, 2111);
  assert.equal(runtime.api.stats().hi, 2111);
  assert.equal(runtime.derivedObjectCreates(), 0, 'English API and coverage stats must leave lookups deferred');
  for (const result of [runtime.api.apply('en'), runtime.api.translate('Save', 'en'), runtime.api.stats()]) {
    assert.equal(result instanceof Promise, false, 'public localization methods must stay synchronous');
  }
});

test('first Marathi or Hindi use builds lookups synchronously and early ST_LANG remains effective', () => {
  const runtime = loadRuntime('en');
  assert.equal(runtime.api.translate('Settings', 'mr'), 'सेटिंग्ज');
  assert.equal(runtime.derivedObjectCreates(), 4, 'first native translation constructs two dictionaries and two word maps');
  assert.equal(runtime.api.translate('Save', 'hi'), 'सहेजें');
  assert.equal(runtime.derivedObjectCreates(), 4, 'derived lookups are constructed only once');
  assert.equal(runtime.listeners['window:message']({ data: { type: 'ST_LANG', lang: 'mr' } }), undefined);
  assert.equal(runtime.document.documentElement.lang, 'mr');

  const earlyNative = loadRuntime('hi');
  assert.equal(earlyNative.document.documentElement.lang, 'en', 'boot waits for DOMContentLoaded');
  earlyNative.listeners.DOMContentLoaded();
  assert.equal(earlyNative.document.documentElement.lang, 'hi');
  assert.equal(earlyNative.api.getLanguage(), 'hi');
});

test('offline API-23 localization remains one static local asset with no dynamic or remote loader', async () => {
  assert.doesNotMatch(source, /document\.createElement\(['"]script['"]\)|https?:\/\//);
  assert.doesNotMatch(source, /\b(?:Promise|fetch|import)\s*\(/);
  for (const html of [shell, ...await Promise.all(moduleNames.map(name => readFile(new URL(`modules/${name}/index.html`, root), 'utf8')))]) {
    const match = html.match(/<script src="([^"]*app-i18n\.js)"><\/script>/);
    assert.ok(match, 'each surface must load app-i18n.js');
    assert.doesNotMatch(match[1], /^(?:https?:)?\/\//, 'localization runtime must stay local/offline');
  }
});

test('runtime localizes dynamic UI but excludes editable and business-data surfaces', () => {
  assert.match(source, /new MutationObserver/);
  assert.match(source, /ST_LANG/);
  assert.match(source, /saagar_lang/);
  for (const guard of ['INPUT', 'TEXTAREA', 'TBODY', 'contenteditable', 'data-no-i18n']) {
    assert.match(source, new RegExp(guard, 'i'), `${guard} guard must remain present`);
  }
  assert.match(source, /data-i18n/);
});

test('shell broadcasts language changes to the active module', async () => {
  const setLang = shell.slice(shell.indexOf('function setLang('), shell.indexOf('/*', shell.indexOf('function setLang(')));
  assert.match(setLang, /applyLangToFrame\(l\)/);
  assert.match(shell, /postMessage\(\{type:'ST_LANG',lang:/);
  const frameController = await readFile(new URL('shared/shell-module-frame-controller.js', root), 'utf8');
  assert.match(frameController, /shell\.applyLangToFrame\(shell\.getLang\(\)\)/);
  const authoritativeRender = shell.slice(shell.indexOf('function doFirstRender(){'), shell.indexOf('// First render'));
  assert.match(authoritativeRender, /applyLang\(\);\s*reflectLangUI\(\)/);
  assert.doesNotMatch(shell, /data-i18n="cfg\.language">Language<\/span>\s*\/\s*भाषा/);
});
