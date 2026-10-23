import assert from 'node:assert/strict';
import { test } from 'node:test';
import { reservationConfirmation } from '../src/experiences/reservation-confirmation.ts';

const booking = { scheduledAt:'2026-10-25T00:00', tablesId:'29', section:'VIP №29, комната целиком', customerName:'Test', customerPhone:'77010000001', remindBeforeHour:false };

test('confirmation identifies the actual midnight date, entire VIP room and one-hour visit', () => {
  const summary = reservationConfirmation(booking, 'The Veil', 60);
  assert.equal(summary.venue, 'The Veil');
  assert.match(summary.date, /25 октября 2026/);
  assert.equal(summary.time, '00:00–01:00');
  assert.equal(summary.resource, 'VIP-комната №29 целиком');
  assert.equal(summary.duration, '1 час');
});

test('a visit crossing New Year identifies the end date and preserves the venue clock', () => {
  const previousZone = process.env.TZ;
  try {
    process.env.TZ = 'America/Los_Angeles';
    const summary = reservationConfirmation({ ...booking, scheduledAt:'2026-12-31T23:00', tablesId:'8', section:'Общий зал' }, 'The Tochka', 180);
    assert.match(summary.date, /31 декабря 2026/);
    assert.match(summary.time, /^23:00–02:00 \(1 января 2027/);
    assert.equal(summary.resource, 'Столик №8 · Общий зал');
    assert.equal(summary.duration, '3 часа');
  } finally {
    if (previousZone === undefined) delete process.env.TZ;
    else process.env.TZ = previousZone;
  }
});

test('the summary retains the submitted booking after the form and selection are reset', () => {
  const submitted = { ...booking, tablesId:'12', section:'Общий зал' };
  const summary = reservationConfirmation(submitted, 'The Veil', 60);
  submitted.tablesId = '';
  submitted.scheduledAt = '';
  assert.equal(summary.resource, 'Столик №12 · Общий зал');
  assert.equal(summary.time, '00:00–01:00');
});

test('an unexpected timestamp does not turn a committed booking into a formatting error', () => {
  const summary = reservationConfirmation({ ...booking, scheduledAt:'unexpected timestamp' }, 'The Veil', 60);
  assert.equal(summary.date, 'unexpected timestamp');
  assert.equal(summary.time, '');
  assert.equal(summary.resource, 'VIP-комната №29 целиком');
});
