import assert from 'node:assert/strict';
import { test } from 'node:test';
import { veilAreas, veilMapBox, veilMapSize } from '../src/experiences/veil-floor-plan-data.ts';

const hall = veilAreas.find(area => area.id === 'hall');
const close = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-8);

test('landscape hall targets follow the clockwise artwork rotation without changing inventory', () => {
  const size = veilMapSize(hall);
  assert.ok(size.width > size.height);
  assert.deepEqual(hall.tables.map(table => table.number), Array.from({length:25}, (_,i)=>i+1));
  for (const table of hall.tables) {
    const box = veilMapBox(hall, table);
    close(box.x * size.width / 100, hall.height - table.y * hall.height / 100);
    close(box.y * size.height / 100, table.x * hall.width / 100);
    close(box.width * size.width / 100, table.height * hall.height / 100);
    close(box.height * size.height / 100, table.width * hall.width / 100);
    assert.equal(table.capacity, table.number >= 12 && table.number <= 23 ? 4 : 6);
  }
});

test('landscape entrance is upper left and VIP stairs lower right', () => {
  const entrance = veilMapBox(hall, hall.landmarks.find(room => room.kind === 'entrance'));
  const stairs = veilMapBox(hall, hall.landmarks.find(room => room.kind === 'stairs'));
  assert.ok(entrance.x < 20 && entrance.y < 20);
  assert.ok(stairs.x > 80 && stairs.y > 80);
});

test('VIP artwork dimensions and table coordinates remain unchanged', () => {
  const vip = veilAreas.find(area => area.id === 'vip');
  assert.deepEqual(veilMapSize(vip), {width:1900,height:920});
  for (const table of vip.tables) assert.deepEqual(veilMapBox(vip, table), table);
  assert.deepEqual(vip.tables.map(table => [table.number,table.capacity]), [[28,6],[29,8]]);
});
