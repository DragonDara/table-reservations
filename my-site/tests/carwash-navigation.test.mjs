import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createNavigationGuard, isChoiceStepAnswered } from '../src/experiences/carwash-navigation.ts';

test('a double tap cannot skip a step or immediately reverse navigation', () => {
  let now = 0;
  const accept = createNavigationGuard(() => now);
  assert.equal(accept(), true);
  now = 120;
  assert.equal(accept(), false);
  now = 349;
  assert.equal(accept(), false);
  now = 350;
  assert.equal(accept(), true);
  now = 351;
  assert.equal(accept(), false);
  now = 1000;
  assert.equal(accept(), true);
});

test('Next requires a choice on category, services and time steps', () => {
  assert.equal(isChoiceStepAnswered('category', '', 0, false), false);
  assert.equal(isChoiceStepAnswered('category', 'car', 0, false), true);
  assert.equal(isChoiceStepAnswered('service', 'car', 0, false), false);
  assert.equal(isChoiceStepAnswered('service', 'car', 2, false), true);
  assert.equal(isChoiceStepAnswered('time', 'car', 2, false), false);
  assert.equal(isChoiceStepAnswered('time', 'car', 2, true), true);
});

test('text and date steps retain submit-time validation', () => {
  for (const step of ['plate', 'date', 'name', 'phone']) {
    assert.equal(isChoiceStepAnswered(step, 'car', 2, true), true);
  }
});
