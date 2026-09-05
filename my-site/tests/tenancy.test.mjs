import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';
import { carwashSlots, kazakhstanDate } from '../src/experiences/carwash-schedule.ts';

let server;
let resolveOrganizationIdFallback;
before(async () => {
  server = await createServer({
    root: fileURLToPath(new URL('..', import.meta.url)),
    configFile: false,
    envDir: false,
    server: { middlewareMode: true, watch: null, ws: false },
  });
  ({ resolveOrganizationIdFallback } = await server.ssrLoadModule('/src/tenancy/tenant.ts'));
});
after(async () => {
  delete globalThis.window;
  await server?.close();
});

function visit(path, stored = null) {
  const storage = new Map(stored ? [['organizationId', stored]] : []);
  globalThis.window = {
    location: new URL(path, 'http://localhost:5173'),
    localStorage: {
      getItem: (key) => storage.get(key) ?? null,
      setItem: (key, value) => storage.set(key, value),
    },
    history: { state: null, replaceState: (_state, _unused, url) => { window.location = url; } },
  };
}

test('carwash URLs beat a saved lounge tenant and conflicting query', () => {
  for (const path of ['/carwash', '/carwash/', '/thetochka-carwasher', '/thetochka-carwasher/']) {
    visit(`${path}?org=thetochka`, 'thetochka');
    assert.equal(resolveOrganizationIdFallback(), 'thetochka-carwasher');
  }
});

test('lounge URLs beat a saved carwash tenant', () => {
  for (const path of ['/lounge', '/lounge/', '/thetochka', '/thetochka/']) {
    visit(path, 'thetochka-carwasher');
    assert.equal(resolveOrganizationIdFallback(), 'thetochka');
  }
});

test('legacy query links remain supported and remembered', () => {
  for (const key of ['org', 'organizationId']) {
    visit(`/?${key}=thetochka-carwasher`, 'thetochka');
    assert.equal(resolveOrganizationIdFallback(), 'thetochka-carwasher');
    assert.equal(window.localStorage.getItem('organizationId'), 'thetochka-carwasher');
  }
});

test('routes work when browser storage is unavailable', () => {
  visit('/carwash');
  window.localStorage.getItem = () => { throw new Error('Blocked storage'); };
  assert.equal(resolveOrganizationIdFallback(), 'thetochka-carwasher');
});

test('tenant hostname with no fallback is left for the API to resolve', () => {
  visit('/');
  window.location = new URL('https://thetochka-carwasher.bron.cafe/');
  assert.equal(resolveOrganizationIdFallback(), null);
});

const hours = {
  startTime: '08:00', endTime: '20:00', slotDurationMinutes: 60,
  availableTimeSlots: ['08:00', '09:00', '10:00', '19:00'],
};

test('carwash offers only configured slots with at least five minutes lead time', () => {
  assert.deepEqual(carwashSlots('2026-09-05', hours, new Date('2026-09-05T03:56:00Z')), [
    '2026-09-05T10:00', '2026-09-05T19:00',
  ]);
  assert.ok(carwashSlots('2026-09-05', hours, new Date('2026-09-05T03:55:00Z')).includes('2026-09-05T09:00'));
});

test('calendar date is Kazakhstan time even across a UTC date boundary', () => {
  const now = new Date('2026-09-05T20:00:00Z');
  assert.equal(kazakhstanDate(now), '2026-09-06');
  assert.deepEqual(carwashSlots('2026-09-05', hours, now), []);
  assert.equal(carwashSlots('2026-09-06', hours, now)[0], '2026-09-06T08:00');
});

test('overnight slots roll into the next calendar day', () => {
  const overnight = { ...hours, startTime: '22:00', endTime: '02:00', availableTimeSlots: ['22:00', '23:00', '00:00', '01:00'] };
  assert.deepEqual(carwashSlots('2026-12-31', overnight, new Date('2026-12-31T00:00:00Z')), [
    '2026-12-31T22:00', '2026-12-31T23:00', '2027-01-01T00:00', '2027-01-01T01:00',
  ]);
});

test('invalid dates, past dates, empty schedules and expired selections have no slots', () => {
  const now = new Date('2026-09-05T15:00:00Z');
  for (const date of ['', 'invalid', '2026-02-30', '2026-09-04', '2026-09-05']) {
    assert.deepEqual(carwashSlots(date, hours, now), []);
  }
  assert.deepEqual(carwashSlots('2026-09-06', { ...hours, availableTimeSlots: [] }, now), []);
});
