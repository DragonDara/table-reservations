// Run after npm run build, from my-site: node tests/smoke.mjs.
// These requests only read the built frontend; no reservations are created.
import assert from 'node:assert/strict';
import { preview } from 'vite';

const server = await preview({
  configFile: false,
  preview: { host: '127.0.0.1', port: 4175, strictPort: true },
});
try {
  for (const route of ['/lounge', '/carwash', '/carwash/', '/thetochka', '/thetochka-carwasher/']) {
    const response = await fetch(`http://127.0.0.1:4175${route}`);
    assert.equal(response.status, 200, route);
    const html = await response.text();
    assert.ok(html.includes('data-app-root'), `${route}: application shell`);
    const assets = [...html.matchAll(/(?:src|href)="(\/assets\/[^" ]+)"/g)].map((match) => match[1]);
    assert.ok(assets.length >= 2, `${route}: built script and styles`);
    for (const asset of assets) {
      const result = await fetch(`http://127.0.0.1:4175${asset}`);
      assert.equal(result.status, 200, asset);
    }
    console.log(`${route}: 200; built assets available`);
  }
} finally {
  await server.close();
}
