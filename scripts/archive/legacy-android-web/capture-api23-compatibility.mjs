import fs from 'node:fs';

const endpoint = process.argv[2] || 'http://127.0.0.1:9222/json';
const output = process.argv[3] || 'verification/api23-code-closure-evidence.json';
const pages = await (await fetch(endpoint)).json();
if (!pages[0]?.webSocketDebuggerUrl) throw new Error('No debuggable API-23 WebView found');
const socket = new WebSocket(pages[0].webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let sequence = 0;
const pending = new Map();
socket.onmessage = ({ data }) => {
  const message = JSON.parse(data);
  if (message.id && pending.has(message.id)) {
    pending.get(message.id)(message);
    pending.delete(message.id);
  }
};
function evaluate(expression, awaitPromise = false) {
  const id = ++sequence;
  socket.send(JSON.stringify({ id, method: 'Runtime.evaluate', params: { expression, awaitPromise, returnByValue: true } }));
  return new Promise((resolve) => pending.set(id, (message) => resolve(message.result?.result?.value)));
}

const base = JSON.parse(await evaluate(`JSON.stringify({
  userAgent:navigator.userAgent,
  api23:/Android 6\\.0/.test(navigator.userAgent),
  webAssembly:typeof WebAssembly==='object',
  capacitor:typeof Capacitor==='object',
  nativeStore:!!(window.Capacitor&&Capacitor.Plugins&&Capacitor.Plugins.SaagarNativeStore),
  secureShell:!!document.querySelector('[aria-label="Admin mode"]')
})`));
const ids = await evaluate(`SaagarModuleManifest.ids.slice()`);
const modules = [];
for (const id of ids) {
  const opened = await evaluate(`(function(){try{var f=document.getElementById('moduleFrame');openModule(${JSON.stringify(id)});return JSON.stringify({ok:true,src:moduleById(${JSON.stringify(id)}).src,frameSrc:f.getAttribute('src')||''});}catch(e){return JSON.stringify({ok:false,error:String(e)});}})()`);
  await new Promise((resolve) => setTimeout(resolve, 1800));
  const value = await evaluate(`(function(){var f=document.getElementById('moduleFrame');var d=f&&f.contentDocument;var le=document.getElementById('moduleLoadError');return JSON.stringify({id:${JSON.stringify(id)},ok:!!(d&&d.body&&d.body.textContent.trim()),title:d?d.title:'',bodyChars:d&&d.body?d.body.textContent.trim().length:0,loadError:!!(le&&!le.classList.contains('hidden'))});})()`);
  const result = JSON.parse(value);
  result.open = JSON.parse(opened);
  modules.push(result);
}
const evidence = { capturedAt: new Date().toISOString(), endpoint, base, modules, passed: base.api23 && base.capacitor && base.nativeStore && modules.length === 12 && modules.every((item) => item.open.ok && item.ok && !item.loadError) };
fs.writeFileSync(output, `${JSON.stringify(evidence, null, 2)}\n`);
socket.close();
console.log(JSON.stringify(evidence, null, 2));
if (!evidence.passed) process.exitCode = 1;
