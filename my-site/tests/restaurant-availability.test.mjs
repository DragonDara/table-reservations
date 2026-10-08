import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createServer } from 'vite';
import { fileURLToPath } from 'node:url';

test('reservation retry keys preserve tenant headers and API calls avoid caching', async () => {
  const originalFetch = globalThis.fetch;
  const originalWindow = globalThis.window;
  const calls = [];
  const server = await createServer({
    root: fileURLToPath(new URL('..', import.meta.url)), configFile: false, envDir: false,
    server: { middlewareMode: true, watch: null, ws: false },
  });
  try {
    globalThis.window = {
      location: new URL('http://localhost:5173/?org=thetochka'),
      localStorage: { getItem: () => null, setItem() {} },
    };
    globalThis.fetch = async (url, init) => {
      calls.push({ url, ...init });
      return Response.json({ success: true });
    };
    const api = await server.ssrLoadModule('/src/api.ts');
    await api.createReservation({ customerName: 'Guest' }, 'retry-key');
    assert.equal(calls[0].headers.get('X-Organization-Id'), 'thetochka');
    assert.equal(calls[0].headers.get('Idempotency-Key'), 'retry-key');
    assert.equal(calls[0].headers.get('Content-Type'), 'application/json');
    const controller = new AbortController();
    await api.getTables('2026-10-09T18:00', controller.signal);
    assert.equal(calls[1].signal, controller.signal);
    assert.equal(calls[1].cache, 'no-store');
    await api.checkReservationStatus('reservation-id');
    assert.equal(calls[2].url, '/api/Reservations/reservation-id/check');
    assert.equal(calls[2].method, 'POST');
  } finally {
    globalThis.fetch = originalFetch;
    globalThis.window = originalWindow;
    await server.close();
  }
});