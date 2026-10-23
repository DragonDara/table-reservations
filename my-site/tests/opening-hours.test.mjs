import assert from 'node:assert/strict';
import { test } from 'node:test';
import { groupOpeningHours } from '../src/tenancy/opening-hours.ts';

test('weekly hours group Monday–Thursday, Friday–Saturday and Sunday without shifting overnight days', () => {
  const hours = Array.from({ length: 7 }, (_, day) => ({
    day, openTime: '12:00', closeTime: day === 5 || day === 6 ? '03:00' : '02:00', closesNextDay: true,
  }));
  assert.deepEqual(groupOpeningHours(hours, 'ru'), [
    { days: 'Пн–Чт', openTime: '12:00', closeTime: '02:00', closesNextDay: true },
    { days: 'Пт–Сб', openTime: '12:00', closeTime: '03:00', closesNextDay: true },
    { days: 'Вс', openTime: '12:00', closeTime: '02:00', closesNextDay: true },
  ]);
  assert.deepEqual(hours.map(hours => hours.day), [0, 1, 2, 3, 4, 5, 6]);
});

test('missing weekdays are not merged into an apparently continuous schedule', () => {
  const hours = [1, 3].map(day => ({ day, openTime: '12:00', closeTime: '02:00', closesNextDay: true }));
  assert.deepEqual(groupOpeningHours(hours).map(group => group.days), ['Пн', 'Ср']);
  assert.deepEqual(groupOpeningHours([]), []);
});
