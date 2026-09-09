import assert from 'node:assert/strict';
import { afterEach, beforeEach, mock, test } from 'node:test';
import { readFile } from 'node:fs/promises';
import { initialErrorWhatsApp } from '../src/tenancy/error-support.ts';

// Small event/DOM fixture: run the real notification module without a browser
// dependency. Layout and screen-reader behavior still require browser QA.
class Element extends EventTarget {
  children = [];
  attributes = new Map();
  hidden = false;
  textUpdates = 0;
  text = '';
  set textContent(value) { this.text = value; this.textUpdates++; }
  get textContent() { return this.text; }
  setAttribute(key, value) { this.attributes.set(key, value); }
  removeAttribute(key) { this.attributes.delete(key); if (key === 'href') delete this.href; }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  focus(options) { this.focusOptions = options; }
}
class Input extends Element { validationMessage = ''; }
class Select extends Input {}
class Textarea extends Input {}

const names = ['document', 'window', 'HTMLInputElement', 'HTMLSelectElement', 'HTMLTextAreaElement'];
let saved;
let notifications;
let moduleId = 0;

beforeEach(async () => {
  saved = names.map(name => Object.getOwnPropertyDescriptor(globalThis, name));
  globalThis.document = Object.assign(new EventTarget(), {
    body: new Element(), createElement: () => new Element(),
  });
  globalThis.window = new EventTarget();
  globalThis.HTMLInputElement = Input;
  globalThis.HTMLSelectElement = Select;
  globalThis.HTMLTextAreaElement = Textarea;
  mock.method(console, 'error', () => {});
  notifications = await import(`../src/ui/error-notification.ts?test=${moduleId++}`);
});
afterEach(() => {
  names.forEach((name, index) => {
    if (saved[index]) Object.defineProperty(globalThis, name, saved[index]);
    else delete globalThis[name];
  });
  mock.restoreAll();
});

function elements() {
  const box = document.body.children.at(-1);
  return { box, message: box.children[0].children[0], whatsApp: box.children[0].children[1], close: box.children[1] };
}
function invalid(input) {
  const event = new Event('invalid', { cancelable: true });
  Object.defineProperty(event, 'target', { value: input });
  document.dispatchEvent(event);
  return event;
}

test('one persistent box survives tenant-root replacement and can be closed and reopened', () => {
  const app = new Element();
  document.body.append(app);
  notifications.initErrorNotifications();
  notifications.initErrorNotifications();
  const { box, message, close } = elements();
  assert.equal(document.body.children.length, 2);
  assert.equal(box.hidden, true);
  assert.equal(message.attributes.get('role'), 'alert');
  assert.ok(close.attributes.get('aria-label'));

  notifications.showError('Не удалось загрузить услуги.');
  assert.equal(box.hidden, false);
  const updates = message.textUpdates;
  notifications.showError('Не удалось загрузить услуги.');
  assert.equal(message.textUpdates, updates, 'duplicate errors must not reannounce');
  app.hidden = true;
  app.replaceChildren(new Element());
  assert.equal(box.hidden, false);
  notifications.showError('Не удалось загрузить бронирования.');
  assert.equal(message.textContent, 'Не удалось загрузить бронирования.');
  close.dispatchEvent(new Event('click'));
  assert.equal(box.hidden, true);
  assert.equal(message.textContent, '');
  notifications.showError('Не удалось загрузить бронирования.');
  assert.equal(box.hidden, false);
  assert.equal(document.body.children.length, 2);
});

test('no auto-dismiss timer is scheduled and errors never focus or scroll the notification', () => {
  const timeout = mock.method(globalThis, 'setTimeout', () => { throw new Error('Unexpected timer'); });
  notifications.showError('Проверьте дату.');
  const { box, message, close } = elements();
  assert.equal(timeout.mock.callCount(), 0);
  assert.equal(box.focusOptions, undefined);
  assert.equal(message.focusOptions, undefined);
  assert.equal(close.focusOptions, undefined);
});

test('network and unexpected exceptions get readable messages; business errors retain their details', () => {
  assert.match(notifications.errorMessage(new TypeError('Failed to fetch')), /Проверьте подключение/);
  assert.match(notifications.errorMessage(new TypeError('Load failed')), /Проверьте подключение/);
  assert.equal(notifications.errorMessage(new Error('SQL credentials stack trace'), 'Попробуйте снова.'), 'Попробуйте снова.');
  const apiError = (status, body) => ({ name: 'ApiError', status, body });
  assert.equal(notifications.errorMessage(apiError(409, { message: 'Столик уже занят.' })), 'Столик уже занят.');
  assert.equal(notifications.errorMessage(apiError(400, 'Выберите дату.')), 'Выберите дату.');
  for (const error of [apiError(500, { message: 'Database password' }), apiError(502, '<html>Bad gateway</html>'), apiError(400, '<html>Error</html>')]) {
    assert.equal(notifications.errorMessage(error, 'Не удалось выполнить запрос.'), 'Не удалось выполнить запрос.');
  }
});

test('runtime failures and rejected promises appear in the same box, without raw exception text', () => {
  notifications.initErrorNotifications();
  const event = new Event('error');
  Object.defineProperty(event, 'error', { value: new Error('internal detail') });
  window.dispatchEvent(event);
  const { box, message, close } = elements();
  assert.equal(box.hidden, false);
  assert.doesNotMatch(message.textContent, /internal detail/);
  close.dispatchEvent(new Event('click'));
  const rejection = new Event('unhandledrejection');
  Object.defineProperty(rejection, 'reason', { value: new TypeError('Failed to fetch') });
  window.dispatchEvent(rejection);
  assert.equal(box.hidden, false);
  assert.match(message.textContent, /Проверьте подключение/);
  assert.equal(document.body.children.length, 1);
});

test('cancelled obsolete requests do not produce an error box', () => {
  notifications.reportError(new DOMException('Cancelled', 'AbortError'));
  assert.equal(document.body.children.length, 0);
});

test('WhatsApp link survives new errors and switches to the loaded tenant contact without changing the message', () => {
  notifications.configureErrorWhatsApp('https://wa.me/77751516189');
  notifications.showError('Не удалось открыть страницу.');
  const { whatsApp, message } = elements();
  assert.equal(whatsApp.hidden, false);
  assert.equal(whatsApp.href, 'https://wa.me/77751516189');
  assert.equal(whatsApp.textContent, 'Написать в WhatsApp');
  assert.equal(whatsApp.target, '_blank');
  assert.equal(whatsApp.rel, 'noopener noreferrer');
  notifications.configureErrorWhatsApp('https://api.whatsapp.com/send/?phone=77475569530');
  assert.equal(whatsApp.href, 'https://api.whatsapp.com/send/?phone=77475569530');
  assert.equal(message.textContent, 'Не удалось открыть страницу.');
  notifications.showError('Выберите дату.');
  assert.equal(whatsApp.hidden, false);
  for (const value of [null, '', 'javascript:alert(1)', 'https://wa.me.evil.example/123']) {
    notifications.configureErrorWhatsApp(value);
    assert.equal(whatsApp.hidden, true);
    assert.equal(whatsApp.href, undefined);
  }
});

test('startup chat addresses match public configuration and respect tenant hosts over saved selections', async () => {
  const settings = JSON.parse(await readFile(new URL('../../table-reservations/appsettings.json', import.meta.url), 'utf8'));
  for (const org of settings.Organizations.Items) {
    assert.equal(initialErrorWhatsApp(`${org.Id}.bron.cafe`, 'other'), org.Frontend.Links.WhatsApp);
    assert.equal(initialErrorWhatsApp(`${org.Id}.localhost`, 'other'), org.Frontend.Links.WhatsApp);
    assert.equal(initialErrorWhatsApp('localhost', org.Id), org.Frontend.Links.WhatsApp);
  }
  assert.equal(initialErrorWhatsApp('unknown.bron.cafe', 'thetochka'), null);
  assert.equal(initialErrorWhatsApp('localhost', null), null);
});

test('native validation suppresses bubbles and reports/focuses only the first invalid field per pass', async () => {
  notifications.initErrorNotifications();
  const name = new Input();
  name.validationMessage = 'Введите имя.';
  const phone = new Input();
  phone.validationMessage = 'Введите телефон.';
  assert.equal(invalid(name).defaultPrevented, true);
  assert.equal(invalid(phone).defaultPrevented, true);
  await Promise.resolve();
  assert.equal(elements().message.textContent, 'Введите имя.');
  assert.deepEqual(name.focusOptions, { preventScroll: true });
  assert.equal(phone.focusOptions, undefined);
  invalid(phone);
  await Promise.resolve();
  assert.equal(elements().message.textContent, 'Введите телефон.');
  assert.deepEqual(phone.focusOptions, { preventScroll: true });
});
