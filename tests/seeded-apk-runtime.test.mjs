import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoDir = path.resolve(here, '..');
const seedSource = fs.readFileSync(path.join(repoDir, 'www', 'demo-seed.js'), 'utf8');

function parseStored(storage, key) {
  const value = storage.get(key);
  assert.notEqual(value, undefined, `expected seeded storage key ${key}`);
  return JSON.parse(value);
}

test('730-day runtime seed creates two-year cross-module data', { timeout: 60_000 }, async () => {
  const storage = new Map();
  let archive = null;
  let seedPromise = null;

  const localStorage = {
    getItem(key) {
      return storage.has(String(key)) ? storage.get(String(key)) : null;
    },
    setItem(key, value) {
      storage.set(String(key), String(value));
    },
    removeItem(key) {
      storage.delete(String(key));
    },
    clear() {
      storage.clear();
    }
  };

  const context = {
    __SEED_DAYS: 730,
    __SEED_WALK: 25,
    localStorage,
    console: { log() {}, warn() {}, error() {} },
    setTimeout,
    clearTimeout,
    document: {
      createElement() {
        throw new Error('DOM is intentionally unavailable in this storage smoke test');
      },
      createEvent() {
        throw new Error('DOM events are intentionally unavailable in this storage smoke test');
      },
      body: {}
    }
  };
  context.window = context;
  context.Capacitor = {
    Plugins: {
      Filesystem: {
        async writeFile({ data }) {
          archive = JSON.parse(data);
        },
        async rename() {}
      }
    }
  };
  context.SaagarStore = {
    bulkAsync(fn) {
      seedPromise = Promise.resolve().then(fn);
      return seedPromise;
    },
    bulk(fn) {
      return fn();
    }
  };

  vm.createContext(context);
  vm.runInContext(seedSource, context, { filename: 'demo-seed.js', timeout: 5_000 });
  assert.ok(seedPromise, 'large seed must use the chunked bulkAsync path');
  await seedPromise;

  assert.equal(storage.get('saagar_demo_seeded'), 'v4_730d_25w');
  const profile = parseStored(storage, 'saagar_demo_profile_v1');
  assert.deepEqual(
    {
      id: profile.id,
      syntheticOnly: profile.syntheticOnly,
      daysBack: profile.daysBack,
      calendarDays: profile.calendarDays,
      walkInsPerWorkingDay: profile.walkInsPerWorkingDay,
      stores: Array.from(profile.stores),
      payrollLockedMonths: profile.payrollLockedMonths
    },
    {
      id: 'two-year-review-v1',
      syntheticOnly: true,
      daysBack: 730,
      calendarDays: 731,
      walkInsPerWorkingDay: 25,
      stores: ['WLMHW', 'HEMW'],
      payrollLockedMonths: 24
    }
  );
  assert.equal(
    Math.round((Date.parse(`${profile.endDate}T12:00:00Z`) - Date.parse(`${profile.startDate}T12:00:00Z`)) / 86_400_000),
    730
  );

  const queue = parseStored(storage, 'retail_queue_management_v1');
  assert.equal(queue.customers.length, profile.qmsLive);
  assert.ok(profile.qmsLive > 1_500, 'recent live QMS partition should be populated at review scale');
  assert.ok(Array.isArray(archive) && archive.length === profile.qmsArchived);
  assert.ok(profile.qmsArchived > 10_000, 'older QMS history should be written to the archive');
  assert.ok(queue.customers.concat(archive).every(customer => /^1\d{9}$/.test(customer.mobile)));

  const service = parseStored(storage, 'saagar_wsf_v2');
  assert.equal(service.length, profile.serviceCases);
  const openService = service.filter(item => item.status === 'open');
  assert.deepEqual(
    new Set(openService.map(item => item.stage)),
    new Set(['received', 'awaiting_approval', 'in_progress', 'ready', 'on_hold'])
  );
  assert.ok(openService.some(item => item.d3Readiness));
  assert.ok(openService.some(item => item.demoScenario === 'REPEAT-DEMO'));
  assert.ok(service.every(item => /^1\d{9}$/.test(item.custMobile)));

  const payroll = parseStored(storage, 'payroll_suite_v1_2026');
  assert.equal(Object.keys(payroll.runs).length, 24);
  assert.ok(Object.values(payroll.runs).every(run => run.status === 'locked'));

  const taxFeed = parseStored(storage, 'gm_tax_feed');
  assert.ok(Object.keys(taxFeed).length >= 23);
  const statements = parseStored(storage, 'tanishq_statements');
  assert.equal(Object.keys(statements).length, 731);
  /* The seed writes stock for every day except Sunday (demo-seed.js isWeekend()
     is `getDay() === 0`). The window is 730 days, and 730 % 7 === 2, so
     profile.startDate sits two weekdays behind today: it lands on a Sunday every
     Tuesday, and profile.endDate lands on one every Sunday. Asserting on the raw
     boundaries therefore failed two days in seven. Anchor on the first and last
     *stocked* day instead, and check the Sunday rule directly. */
  const windowDays = [];
  const cursor = new Date(`${profile.startDate}T12:00:00`);
  for (let i = 0; i < profile.calendarDays; i++) {
    const y = cursor.getFullYear();
    const m = String(cursor.getMonth() + 1).padStart(2, '0');
    const d = String(cursor.getDate()).padStart(2, '0');
    windowDays.push(`${y}-${m}-${d}`);
    cursor.setDate(cursor.getDate() + 1);
  }
  assert.equal(windowDays[0], profile.startDate);
  assert.equal(windowDays[windowDays.length - 1], profile.endDate);

  const isSunday = ds => new Date(`${ds}T12:00:00`).getDay() === 0;
  const stockedDays = windowDays.filter(ds => !isSunday(ds));
  const sundays = windowDays.filter(isSunday);
  assert.ok(stockedDays.length > 600, 'the window should contain a full stock history');
  assert.ok(sundays.length > 90, 'the window should contain Sundays to exclude');

  // Both stores are stocked at each end of the window.
  for (const store of ['titanworld', 'helios']) {
    for (const day of [stockedDays[0], stockedDays[stockedDays.length - 1]]) {
      assert.ok(storage.has(`saagar_stock_${store}_${day}`),
        `expected seeded stock for ${store} on ${day}`);
    }
    // Every non-Sunday is stocked, and no Sunday is.
    const seeded = [...storage.keys()].filter(key => key.startsWith(`saagar_stock_${store}_`));
    assert.equal(seeded.length, stockedDays.length,
      `${store} should have one stock record per non-Sunday`);
    const seededSunday = sundays.find(ds => storage.has(`saagar_stock_${store}_${ds}`));
    assert.equal(seededSunday, undefined, `${store} must not be stocked on ${seededSunday}`);
  }
});
