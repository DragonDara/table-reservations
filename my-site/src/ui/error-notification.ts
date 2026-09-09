const DEFAULT_MESSAGE = 'Произошла ошибка. Попробуйте ещё раз или обновите страницу.';
const NETWORK_MESSAGE = 'Не удалось связаться с сервером. Проверьте подключение и попробуйте снова.';

let box: HTMLDivElement | undefined;
let message: HTMLParagraphElement | undefined;
let initialized = false;

// Keep raw exceptions and server diagnostics out of the visible notification.
export function errorMessage(error: unknown, fallback = DEFAULT_MESSAGE): string {
  if (error && typeof error === 'object') {
    const value = error as { name?: string; message?: string; status?: number; body?: unknown };
    if (value.name === 'TypeError' && /fetch|network|load failed/i.test(value.message ?? '')) {
      return NETWORK_MESSAGE;
    }
    if (value.name === 'ApiError' && value.status && value.status >= 400 && value.status < 500) {
      const body = value.body;
      const detail = body && typeof body === 'object' ? (body as { message?: unknown }).message : body;
      if (typeof detail === 'string' && detail.trim() && detail.length <= 500 && !/[<>]/.test(detail)) {
        return detail.trim();
      }
    }
  }
  return fallback;
}

export function showError(text: string): void {
  initErrorNotifications();
  const nextMessage = text.trim() || DEFAULT_MESSAGE;
  if (!box!.hidden && message!.textContent === nextMessage) return;
  box!.hidden = false;
  message!.textContent = nextMessage;
}

export function reportError(error: unknown, fallback = DEFAULT_MESSAGE): void {
  // Cancellation of obsolete work is not a user-facing failure.
  if (error && typeof error === 'object' && 'name' in error && error.name === 'AbortError') return;
  console.error(fallback, error);
  showError(errorMessage(error, fallback));
}

export function initErrorNotifications(): void {
  if (initialized) return;
  box = document.createElement('div');
  box.className = 'app-error';
  box.hidden = true;
  message = document.createElement('p');
  message.className = 'app-error-message';
  message.setAttribute('role', 'alert');
  message.setAttribute('aria-live', 'assertive');
  message.setAttribute('aria-atomic', 'true');
  const close = document.createElement('button');
  close.type = 'button';
  close.className = 'app-error-close';
  close.setAttribute('aria-label', 'Закрыть сообщение об ошибке');
  close.textContent = '×';
  close.addEventListener('click', () => {
    box!.hidden = true;
    message!.textContent = '';
  });
  box.append(message, close);
  // Tenant pages replace/hide their app root during startup and navigation.
  document.body.append(box);
  initialized = true;

  window.addEventListener('error', (event) => reportError(event.error));
  window.addEventListener('unhandledrejection', (event) => reportError(event.reason));

  // Native validation still blocks submission; replace its popup with our box.
  // A validation pass can emit multiple invalid events: focus/report the first.
  let firstInvalid: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | undefined;
  document.addEventListener('invalid', (event) => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement || input instanceof HTMLSelectElement || input instanceof HTMLTextAreaElement)) return;
    event.preventDefault();
    if (firstInvalid) return;
    firstInvalid = input;
    queueMicrotask(() => {
      const target = firstInvalid!;
      firstInvalid = undefined;
      showError(target.validationMessage || 'Проверьте заполнение поля.');
      target.focus({ preventScroll: true });
    });
  }, true);
}
