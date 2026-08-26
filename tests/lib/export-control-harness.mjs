import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const source = fs.readFileSync(path.resolve(here, '../../www/export-control.js'), 'utf8');

export function loadExportControl(options = {}) {
  const values = new Map(Object.entries(options.seed || {}));
  const notices = [];
  const audit = [];
  let reauthCalls = 0;
  let writeCalls = 0;

  const localStorage = {
    getItem(key) {
      return values.has(key) ? values.get(key) : null;
    },
    setItem(key, value) {
      writeCalls += 1;
      if (options.failWrites === true || options.failWriteAt === writeCalls) {
        throw new Error('injected write failure');
      }
      values.set(key, String(value));
    },
    removeItem(key) {
      values.delete(key);
    }
  };
  const root = {
    localStorage,
    hasAdminPin: () => options.hasPin !== false,
    SaagarReauth: reason => {
      reauthCalls += 1;
      return typeof options.reauth === 'function' ? options.reauth(reason) : options.reauth !== false;
    },
    ownerName: () => options.ownerName || 'Test Owner',
    toast: message => notices.push(String(message)),
    auditLog: (action, detail) => audit.push({ action, detail })
  };

  if (options.safeSet) root.safeSet = options.safeSet;
  if (options.safeGet) root.safeGet = options.safeGet;
  if (options.deviceSecurity !== undefined) {
    root.SaagarDeviceSecurity = { allowSensitive: () => options.deviceSecurity };
  }

  vm.runInNewContext(source, { window: root, globalThis: root }, { filename: 'export-control.js' });

  return {
    api: root.SaagarExportControl,
    values,
    notices,
    audit,
    reauthCalls: () => reauthCalls,
    writes: () => writeCalls,
    readRegister() {
      const raw = values.get('st_v2_export_register_v1');
      return raw ? JSON.parse(raw) : [];
    },
    readPolicy() {
      const raw = values.get('st_v2_export_policy_v1');
      return raw ? JSON.parse(raw) : null;
    }
  };
}
