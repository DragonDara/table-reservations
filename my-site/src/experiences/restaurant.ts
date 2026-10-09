import {
  ApiError,
  createReservation,
  getAvailableSlots,
  getRating,
  getTables,
  checkReservationStatus,
  type ReservationPayload,
  type TableAvailability,
} from '../api';
import type { PublicTenantConfig } from '../tenancy/types';
import { reportError, showError } from '../ui/error-notification';
import {
  formatKazakhstanPhone,
  isKazakhstanMobile,
  KZ_PHONE_INVALID_MESSAGE,
  toE164Digits,
} from '../phone';

// Imported only after the restaurant page and tenant configuration are ready.
export async function initRestaurantExperience(config: PublicTenantConfig): Promise<void> {
  configureBookingTime(config);
  if (config.features.showRating) void loadRating();
  await initRestaurantBooking();
  bookingStorageKey = `restaurant-booking:${config.organizationId}`;
  try {
    const saved = JSON.parse(sessionStorage.getItem(bookingStorageKey) ?? 'null');
    if (saved && typeof saved.key === 'string' && typeof saved.hash === 'string') bookingAttempt = saved;
    if (bookingAttempt?.id) resumePendingBooking(bookingAttempt.id);
  } catch {}
}

const bookBtn = document.getElementById('bookBtn') as HTMLButtonElement | null;
const bookBtn2 = document.getElementById('bookBtn2') as HTMLButtonElement | null;
const navToggle = document.getElementById('navtoggle') as HTMLButtonElement | null;
const nav = document.querySelector<HTMLElement>('.nav');
const navLinks = document.querySelectorAll<HTMLElement>('.nav-link');

bookBtn?.addEventListener('click', () => {
  const reservationSection = document.querySelector<HTMLElement>('.reservation');
  reservationSection?.scrollIntoView({ behavior: 'smooth', block: 'start' });
});

bookBtn2?.addEventListener('click', () => {
  const featuresSection = document.querySelector<HTMLElement>('.how-it-works');
  featuresSection?.scrollIntoView({ behavior: 'smooth' , block: 'start'})
})

navToggle?.addEventListener('click', () => {
  if (!nav) return;

  const isOpen = nav.classList.toggle('nav-open');
  navToggle.setAttribute('aria-expanded', String(isOpen));
});

navLinks.forEach((link) => {
  link.addEventListener('click', () => {
    if (window.innerWidth <= 900) {
      nav?.classList.remove('nav-open');
      navToggle?.setAttribute('aria-expanded', 'false');
    }
  });
});

async function loadRating() {
  const block = document.getElementById('dgisRatingBlock');
  const loader = document.getElementById('dgisRatingLoader');
  const values = document.getElementById('dgisRatingValues');
  const status = document.getElementById('dgisRatingStatus');
  const ratingEl = document.getElementById('dgisRating');
  const reviewsEl = document.getElementById('dgisReviews');

  block?.setAttribute('aria-busy', 'true');
  if (loader) loader.hidden = false;
  if (values) values.hidden = true;
  if (status) status.textContent = 'Загружаем рейтинг 2ГИС';

  try {
    const data = await getRating();
    const rating = Number(data?.rating ?? 0);
    const reviewCount = Number(data?.reviewCount ?? 0);

    if (ratingEl) ratingEl.textContent = Number.isFinite(rating) ? rating.toFixed(1) : '0.0';
    if (reviewsEl) reviewsEl.textContent = Number.isFinite(reviewCount) ? String(reviewCount) : '0';
    if (status) status.textContent = 'Рейтинг 2ГИС загружен';
  } catch (err) {
    reportError(err, 'Не удалось загрузить рейтинг. Попробуйте обновить страницу.');
    if (ratingEl) ratingEl.textContent = '—';
    if (reviewsEl) reviewsEl.textContent = '—';
    if (status) status.textContent = '';
  } finally {
    block?.setAttribute('aria-busy', 'false');
    if (loader) loader.hidden = true;
    if (values) values.hidden = false;
  }
}

// бронирование: заготовка под будущую карту столиков
const openTablePickerBtn = document.getElementById('openTablePicker') as HTMLButtonElement | null;
const selectedTableBadge = document.getElementById('selectedTableBadge') as HTMLElement | null;

const tablePlaceholder = document.querySelector<HTMLElement>('.table-picker-placeholder');

// модалка выбора столика

const overlay = document.getElementById('tableModalOverlay') as HTMLElement | null;
const viewport = document.getElementById('floorPlanViewport') as HTMLElement | null;
const canvas = document.getElementById('floorPlanCanvas') as HTMLElement | null;
const zoomInBtn = document.getElementById('zoomInBtn') as HTMLButtonElement | null;
const zoomOutBtn = document.getElementById('zoomOutBtn') as HTMLButtonElement | null;
const closeModalBtn = document.getElementById('closeTableModal') as HTMLButtonElement | null;
const mobileCloseModalBtn = document.getElementById('mobileCloseTableModal') as HTMLButtonElement | null;
const successModalOverlay = document.getElementById('successModalOverlay') as HTMLElement | null;
const successModalText = document.getElementById('successModalText') as HTMLElement | null;
const successModalCloseBtn = document.getElementById('successModalCloseBtn') as HTMLButtonElement | null;
const dateStep = document.getElementById('dateStep') as HTMLElement | null;
const timeStep = document.getElementById('timeStep') as HTMLElement | null;
const tableStep = document.getElementById('tableStep') as HTMLElement | null;
const nameStep = document.getElementById('nameStep') as HTMLElement | null;
const phoneStep = document.getElementById('phoneStep') as HTMLElement | null;
const continueToTimeBtn = document.getElementById('continueToTime') as HTMLButtonElement | null;
const continueToTableBtn = document.getElementById('continueToTable') as HTMLButtonElement | null;
const continueToNameBtn = document.getElementById('continueToName') as HTMLButtonElement | null;
const continueToPhoneBtn = document.getElementById('continueToPhone') as HTMLButtonElement | null;
let scale = 1;
let panX = 0;
let panY = 0;
const MIN_SCALE = 0.6;
const MAX_SCALE = 4.2;


function applyTransform() {
  if (canvas) {
    canvas.style.transform = `translate(${panX}px, ${panY}px) scale(${scale})`;
  }
}

function setScale(next: number) {
  scale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, next));
  applyTransform();
}

zoomInBtn?.addEventListener('click', () => setScale(scale + 0.2));
zoomOutBtn?.addEventListener('click', () => setScale(scale - 0.2));

viewport?.addEventListener('wheel', (e) => {
  e.preventDefault();

  if (e.ctrlKey) {
    // жест сведения/разведения пальцев — зум
    setScale(scale + (e.deltaY < 0 ? 0.1 : -0.1));
  } else {
    // обычный свайв двумя пальцами — панорамирование
    panX -= e.deltaX;
    panY -= e.deltaY;
    applyTransform();
  }
});

function centerCanvas() {
  if (!viewport || !canvas) return;

  const viewportWidth = viewport.clientWidth;
  const viewportHeight = viewport.clientHeight;
  const canvasWidth = canvas.offsetWidth;
  const canvasHeight = canvas.offsetHeight;

  panX = (viewportWidth - canvasWidth * scale) / 2;
  panY = (viewportHeight - canvasHeight * scale) / 2;
  applyTransform();
}

function centerActiveScene() {
  if (!viewport || !canvas) return;

  const activeScene = document.querySelector<HTMLElement>('.floor-plan-scene.active');
  if (!activeScene) {
    centerCanvas();
    return;
  }

  const viewportWidth = viewport.clientWidth;
  const viewportHeight = viewport.clientHeight;
  const canvasWidth = canvas.offsetWidth;
  const canvasHeight = canvas.offsetHeight;

  if (activeScene.dataset.scene === 'hall') {
    const fitScale = Math.min(
      Math.min(viewportWidth / canvasWidth, viewportHeight / canvasHeight),
      1,
    );
    scale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, fitScale));
    panX = (viewportWidth - canvasWidth * scale) / 2;
    panY = (viewportHeight - canvasHeight * scale) / 2;
    applyTransform();
    return;
  }

  const table = activeScene.querySelector<HTMLElement>('.table-marker');
  if (!table) {
    centerCanvas();
    return;
  }

  const tableCenterX = table.offsetLeft + table.offsetWidth / 2;
  const tableCenterY = table.offsetTop + table.offsetHeight / 2;

  panX = viewportWidth / 2 - tableCenterX * scale;
  panY = viewportHeight / 2 - tableCenterY * scale;
  applyTransform();
}

// перетаскивание (пан) мышью
let isDragging = false;
let dragStartX = 0;
let dragStartY = 0;

viewport?.addEventListener('mousedown', (e) => {
  isDragging = true;
  dragStartX = e.clientX - panX;
  dragStartY = e.clientY - panY;
  viewport.classList.add('dragging');
});

window.addEventListener('mousemove', (e) => {
  if (!isDragging) return;
  panX = e.clientX - dragStartX;
  panY = e.clientY - dragStartY;
  applyTransform();
});

window.addEventListener('mouseup', () => {
  isDragging = false;
  viewport?.classList.remove('dragging');
});

// перетаскивание и зум пальцем (touch)
function getTouchDistance(touches: TouchList): number {
  const dx = touches[0].clientX - touches[1].clientX;
  const dy = touches[0].clientY - touches[1].clientY;
  return Math.sqrt(dx * dx + dy * dy);
}

let pinchStartDist = 0;
let pinchStartScale = 1;
let pinchStartPanX = 0;
let pinchStartPanY = 0;
let pinchStartMidX = 0;
let pinchStartMidY = 0;

function getTouchMidpoint(touches: TouchList): { x: number; y: number } {
  const rect = viewport?.getBoundingClientRect();
  const offsetX = rect?.left ?? 0;
  const offsetY = rect?.top ?? 0;
  return {
    x: (touches[0].clientX + touches[1].clientX) / 2 - offsetX,
    y: (touches[0].clientY + touches[1].clientY) / 2 - offsetY,
  };
}

viewport?.addEventListener('touchstart', (e) => {
  if (e.touches.length === 1) {
    isDragging = true;
    dragStartX = e.touches[0].clientX - panX;
    dragStartY = e.touches[0].clientY - panY;
  } else if (e.touches.length === 2) {
    isDragging = false;
    pinchStartDist = getTouchDistance(e.touches);
    pinchStartScale = scale;
    pinchStartPanX = panX;
    pinchStartPanY = panY;
    const mid = getTouchMidpoint(e.touches);
    pinchStartMidX = mid.x;
    pinchStartMidY = mid.y;
  }
}, { passive: true });

viewport?.addEventListener('touchmove', (e) => {
  if (e.touches.length === 1 && isDragging) {
    e.preventDefault();
    panX = e.touches[0].clientX - dragStartX;
    panY = e.touches[0].clientY - dragStartY;
    applyTransform();
  } else if (e.touches.length === 2) {
    e.preventDefault();

    const dist = getTouchDistance(e.touches);
    const nextScale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, pinchStartScale * (dist / pinchStartDist)));

    // точка канваса, которая была между пальцами в начале жеста —
    // должна остаться под пальцами и после изменения масштаба
    const canvasPointX = (pinchStartMidX - pinchStartPanX) / pinchStartScale;
    const canvasPointY = (pinchStartMidY - pinchStartPanY) / pinchStartScale;

    const mid = getTouchMidpoint(e.touches);
    scale = nextScale;
    panX = mid.x - canvasPointX * nextScale;
    panY = mid.y - canvasPointY * nextScale;
    applyTransform();
  }
}, { passive: false });


viewport?.addEventListener('touchend', () => {
  isDragging = false;
});

// открытие / закрытие модалки
openTablePickerBtn?.addEventListener('click', () => {
  if (overlay) overlay.hidden = false;
  void refreshTableStatuses();
  scale = 1;
  panX = 0;
  panY = 0;
  applyTransform();
  centerCanvas();
});

function closeTableModal() {
  if (!overlay) return;
  closeTablePopup();
  overlay.hidden = true;
}

closeModalBtn?.addEventListener('click', closeTableModal);
mobileCloseModalBtn?.addEventListener('click', closeTableModal);

overlay?.addEventListener('click', (e) => {
  if (e.target === overlay) closeTableModal();
});

// выбор столика
// статусы столиков: занято скоро / занято с ограничением / свободно ---

const tablePopup = document.getElementById('tablePopup') as HTMLElement | null;
const tablePopupIcon = document.getElementById('tablePopupIcon') as HTMLElement | null;
const tablePopupTitle = document.getElementById('tablePopupTitle') as HTMLElement | null;
const tablePopupText = document.getElementById('tablePopupText') as HTMLElement | null;
const tablePopupActions = document.getElementById('tablePopupActions') as HTMLElement | null;

function closeTablePopup() {
  if (tablePopup) tablePopup.hidden = true;
}

function formatHoursFromNow(hours: number): string {
  const selectedSlot = (document.getElementById('datetime') as HTMLInputElement | null)?.value;
  const target = selectedSlot
    ? new Date(`${selectedSlot}:00Z`)
    : new Date();
  target.setTime(target.getTime() + hours * 60 * 60 * 1000);

  // The selected timestamp is already Kazakhstan wall-clock time. UTC is used
  // here only as a timezone-neutral calendar calculator.
  return target.toLocaleTimeString('ru-RU', {
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'UTC',
  });
}

const phoneInput = document.getElementById('phone') as HTMLInputElement | null;
const nameInput = document.getElementById('name') as HTMLInputElement | null;

phoneInput?.addEventListener('input', () => {
  const cursorWasAtEnd = phoneInput.selectionEnd === phoneInput.value.length;
  phoneInput.value = formatKazakhstanPhone(phoneInput.value);
  if (cursorWasAtEnd) {
    phoneInput.setSelectionRange(phoneInput.value.length, phoneInput.value.length);
  }
});

phoneInput?.addEventListener('beforeinput', (e) => {
  if (e.inputType !== 'deleteContentBackward') return;
  if (phoneInput.selectionStart !== phoneInput.selectionEnd) return; // если уже что-то выделено вручную — не мешаем

  const pos = phoneInput.selectionStart ?? 0;
  if (pos === 0) return;

  let start = pos - 1;
  // идём назад, пропуская скобки/пробелы, пока не найдём цифру, которую реально надо стереть
  while (start > 0 && !/\d/.test(phoneInput.value[start])) {
    start--;
  }

  e.preventDefault(); // сами решаем, что удалить, не даём браузеру стереть только скобку

  const newRaw = phoneInput.value.slice(0, start) + phoneInput.value.slice(pos);
  phoneInput.value = formatKazakhstanPhone(newRaw);
  phoneInput.setSelectionRange(phoneInput.value.length, phoneInput.value.length);
});

let selectedTable: HTMLButtonElement | null = null;

function clearSelectedTable() {
  selectedTable?.classList.remove('selected');
  selectedTable = null;

  if (selectedTableBadge && tablePlaceholder) {
    selectedTableBadge.hidden = true;
    selectedTableBadge.textContent = '';
    tablePlaceholder.hidden = false;
  }
  if (continueToNameBtn) continueToNameBtn.disabled = true;
  if (submitButton) submitButton.disabled = true;
}

function selectTable(marker: HTMLButtonElement) {
  if (!['free', 'limited'].includes(marker.dataset.status ?? '')) return;
  selectedTable?.classList.remove('selected');
  selectedTable = marker;
  marker.classList.add('selected');

  const id = marker.dataset.id ?? '';
  const isVip = marker.classList.contains('vip-table');
  if (selectedTableBadge && tablePlaceholder) {
    selectedTableBadge.textContent = isVip ? `VIP №${id}` : `Столик №${id}`;
    selectedTableBadge.hidden = false;
    tablePlaceholder.hidden = true;
  }

  if (continueToNameBtn) continueToNameBtn.disabled = false;
  if (submitButton) submitButton.disabled = submitting || Boolean(pendingBookingId);
  closeTableModal();
  continueToNameBtn?.focus();
}

function showBlockedPopup(marker?: HTMLButtonElement) {
  if (!tablePopup || !tablePopupIcon || !tablePopupTitle || !tablePopupText || !tablePopupActions) return;

  const nextReservationHours = marker?.dataset.nextReservationHours
    ? parseFloat(marker.dataset.nextReservationHours)
    : null;

  tablePopupIcon.textContent = '✕';
  tablePopupIcon.className = 'table-popup-icon icon-blocked';
  tablePopupTitle.textContent = 'Не свободен';

  if (nextReservationHours !== null) {
    const bookedAt = formatHoursFromNow(nextReservationHours);
    tablePopupText.textContent = `Этот столик уже забронирован другими гостями. Бронь уже занята на ${bookedAt}. Выберите другой столик или другое время.`;
  } else {
    tablePopupText.textContent = 'Этот столик занят или забронирован. Выберите другой столик или другое время.';
  }

  tablePopupActions.innerHTML = '';
  const okBtn = document.createElement('button');
  okBtn.type = 'button';
  okBtn.className = 'popup-btn-primary';
  okBtn.textContent = 'Я понял(а)';
  okBtn.addEventListener('click', closeTablePopup);
  tablePopupActions.appendChild(okBtn);

  tablePopup.hidden = false;
}


function showLimitedTimePopup(marker: HTMLButtonElement, gapHours: number) {
  if (!tablePopup || !tablePopupIcon || !tablePopupTitle || !tablePopupText || !tablePopupActions) return;

  const freeUntil = formatHoursFromNow(gapHours);

  tablePopupIcon.textContent = '!';
  tablePopupIcon.className = 'table-popup-icon icon-warning';
  tablePopupTitle.textContent = 'Ограниченное время';
  tablePopupText.textContent = `На ${freeUntil} столик уже забронирован другим гостем. Просим освободить его к этому времени.`;

  tablePopupActions.innerHTML = '';

  const otherBtn = document.createElement('button');
  otherBtn.type = 'button';
  otherBtn.className = 'popup-btn-secondary';
  otherBtn.textContent = 'Выбрать другой';
  otherBtn.addEventListener('click', closeTablePopup);

  const confirmBtn = document.createElement('button');
  confirmBtn.type = 'button';
  confirmBtn.className = 'popup-btn-primary';
  confirmBtn.textContent = 'Подтвердить';
  confirmBtn.addEventListener('click', () => {
    selectTable(marker);
    closeTablePopup();
  });

  tablePopupActions.appendChild(otherBtn);
  tablePopupActions.appendChild(confirmBtn);

  tablePopup.hidden = false;
}

const tableMarkers = document.querySelectorAll<HTMLButtonElement>('.table-marker');
let tableRequestRevision = 0;
let slotRequestRevision = 0;
let expiryTimer: ReturnType<typeof setTimeout> | undefined;
let availabilityErrorShown = false;

function applyTableStatuses(tables: TableAvailability[], preserveKnownOccupied = false) {
  clearTimeout(expiryTimer);
  const now = Date.now();
  let validUntil = now + 30_000;
  const byId = new Map(tables.map(table => [String(table.id), table]));
  tableMarkers.forEach(marker => {
    const info = byId.get(marker.dataset.id ?? '');
    const expires = info?.availabilityValidUntil ? Date.parse(info.availabilityValidUntil) : now + 30_000;
    const hasFreshStatus = info && Number.isFinite(expires) && expires > now;
    const preserveOccupied = !hasFreshStatus && preserveKnownOccupied && marker.dataset.status === 'occupied';
    const status = hasFreshStatus ? info.status : preserveOccupied ? 'occupied' : 'unknown';
    if (info) validUntil = Math.min(validUntil, expires);
    marker.dataset.status = status;
    marker.disabled = status === 'unknown';
    marker.setAttribute('aria-disabled', String(status === 'occupied' || status === 'unknown'));
    const label = preserveOccupied ? 'Не свободен по последней проверке; данные устарели'
      : status === 'occupied' ? 'Не свободен' : status === 'unknown' ? 'Нет актуальных данных' : 'Свободен';
    marker.title = `Столик ${marker.dataset.id}: ${label}`;
    marker.setAttribute('aria-label', marker.title);
    if (info?.nextReservationHours != null) marker.dataset.nextReservationHours = String(info.nextReservationHours);
    else delete marker.dataset.nextReservationHours;
  });
  if (selectedTable && selectedTable.dataset.status === 'occupied' && !submitting && !pendingBookingId) {
    clearSelectedTable();
    setReservationStatus('Выбранный столик недоступен. Выберите другой столик.');
  } else if (selectedTable && selectedTable.dataset.status === 'unknown' && !submitting && !pendingBookingId) {
    setReservationStatus('Проверяем доступность выбранного столика…');
  } else if (reservationStatus?.textContent === 'Проверяем доступность выбранного столика…') {
    setReservationStatus('');
  }
  if (submitButton) setFormBusy(submitting);
  if (tables.length) expiryTimer = setTimeout(() => {
    applyTableStatuses([], true);
    void refreshTableStatuses();
  }, Math.max(0, validUntil - now));
}

async function refreshTableStatuses() {
  const revision = ++tableRequestRevision;
  const selectedTime = datetimeInput?.value;
  try {
    if (revision !== tableRequestRevision || selectedTime !== datetimeInput?.value) return false;
    const tables = await getTables(selectedTime || undefined);
    if (!tables || revision !== tableRequestRevision || selectedTime !== datetimeInput?.value || document.hidden) return false;
    applyTableStatuses(tables);
    availabilityErrorShown = false;
    return true;
  } catch (error) {
    if (revision !== tableRequestRevision || selectedTime !== datetimeInput?.value || document.hidden) return false;
    applyTableStatuses([], true);
    if (!availabilityErrorShown) reportError(error, 'Не удалось проверить столики. Бронирование временно недоступно.');
    availabilityErrorShown = true;
    return false;
  }
}

async function refreshAvailableSlots() {
  const date = dateOptions?.querySelector<HTMLButtonElement>('.date-option.active')?.dataset.date;
  if (!date || document.hidden || !timeStep || timeStep.hidden) return false;
  const revision = ++slotRequestRevision;
  timeOptions?.setAttribute('aria-busy', 'true');
  try {
    const activeDate = dateOptions?.querySelector<HTMLButtonElement>('.date-option.active')?.dataset.date;
    if (revision !== slotRequestRevision || date !== activeDate || timeStep.hidden) return false;
    const slots = await getAvailableSlots(date);
    if (!slots || revision !== slotRequestRevision || document.hidden || timeStep.hidden) return false;
    applyTimeSlots(slots);
    return true;
  } catch {
    if (revision === slotRequestRevision) expireTimeSlots();
    return false;
  }
}

tableMarkers.forEach((marker) => {
  marker.addEventListener('click', () => {
    if (marker.dataset.status === 'unknown') return;
    if (marker.dataset.status === 'occupied') {
      showBlockedPopup(marker);
      return;
    }

    const nextReservationHours = marker.dataset.nextReservationHours
      ? parseFloat(marker.dataset.nextReservationHours)
      : null;

    if (nextReservationHours !== null) {
      showLimitedTimePopup(marker, nextReservationHours);
      return;
    }

    selectTable(marker);
  });
});

const reservationForm = document.getElementById('reservationForm') as HTMLFormElement | null;
const submitButton = reservationForm?.querySelector<HTMLButtonElement>('button[type="submit"]') ?? null;
const reservationStatus = document.getElementById('reservationStatus') as HTMLParagraphElement | null;
const datetimeInput = document.getElementById('datetime') as HTMLInputElement | null;
const dateOptions = document.getElementById('dateOptions') as HTMLElement | null;
const timeOptions = document.getElementById('timeOptions') as HTMLElement | null;
const slotStatus = document.getElementById('slotStatus') as HTMLElement | null;
const checkStatusBtn = document.getElementById('checkReservationStatus') as HTMLButtonElement | null;
const reservationDateInput = document.getElementById('reservationDate') as HTMLInputElement | null;
const reservationTimeInput = document.getElementById('reservationTime') as HTMLSelectElement | null;
const bookingTimeHint = document.getElementById('bookingTimeHint');
const bookingStartTime = document.getElementById('bookingStartTime') as HTMLTimeElement | null;
const bookingEndTime = document.getElementById('bookingEndTime') as HTMLTimeElement | null;
let minimumDateTimeValue = '';
let bookingTimeSlots: string[] = [];
let slotExpiryTimer: ReturnType<typeof setTimeout> | undefined;
let submitting = false;
let pendingBookingId: string | null = null;
let bookingStorageKey = '';
let bookingAttempt: { key: string; hash: string; id?: string } | null = null;

function persistBookingAttempt() {
  try {
    if (bookingAttempt) sessionStorage.setItem(bookingStorageKey, JSON.stringify(bookingAttempt));
    else sessionStorage.removeItem(bookingStorageKey);
  } catch {}
}

function bookingConfirmed(message: string) {
  pendingBookingId = null;
  bookingAttempt = null;
  persistBookingAttempt();
  if (checkStatusBtn) checkStatusBtn.hidden = true;
  if (successModalOverlay && successModalText) {
    successModalText.textContent = message;
    successModalOverlay.hidden = false;
  }
  resetReservationFormUi();
  setReservationStatus(message, 'success');
}

function resumePendingBooking(id: string) {
  pendingBookingId = id;
  if (bookingAttempt) bookingAttempt.id = id;
  persistBookingAttempt();
  setFormBusy(false);
  if (checkStatusBtn) checkStatusBtn.hidden = false;
  setReservationStatus('Результат отправки неизвестен. Нажмите «Проверить статус».');
}

let checkingBookingStatus = false;

async function checkPendingBooking() {
  const id = pendingBookingId;
  if (!id || checkingBookingStatus || !checkStatusBtn) return;
  checkingBookingStatus = true;
  checkStatusBtn.disabled = true;
  setReservationStatus('Проверяем результат отправки в кассу.');
  try {
    const response = await checkReservationStatus(id);
    if (pendingBookingId !== id) return;
    if (response.status === 'Confirmed' && response.success === true) {
      bookingConfirmed('Бронь подтверждена.');
    } else if (response.status === 'Rejected') {
      pendingBookingId = null;
      bookingAttempt = null;
      persistBookingAttempt();
      checkStatusBtn.hidden = true;
      setFormBusy(false);
      setReservationStatus('Касса отклонила бронь. Выберите другое время.', 'error');
      void refreshTableStatuses();
    } else {
      setReservationStatus('Бронь пока обрабатывается. Проверьте статус позже.');
    }
  } catch {
    if (pendingBookingId === id) setReservationStatus('Не удалось проверить статус. Повторите проверку позже.', 'error');
  } finally {
    checkingBookingStatus = false;
    if (checkStatusBtn) {
      checkStatusBtn.disabled = false;
      checkStatusBtn.hidden = !pendingBookingId;
    }
  }
}

checkStatusBtn?.addEventListener('click', () => { void checkPendingBooking(); });

window.addEventListener('pagehide', () => {
  tableRequestRevision++;
  slotRequestRevision++;
  clearTimeout(expiryTimer);
  clearTimeout(slotExpiryTimer);
});
window.addEventListener('pageshow', (event) => {
  if (!event.persisted) return;
  applyTableStatuses([], true);
  void refreshTableStatuses();
  expireTimeSlots();
});

const bookingSteps = [dateStep, timeStep, tableStep, nameStep, phoneStep];

function showBookingStep(step: HTMLElement | null) {
  clearTimeout(slotExpiryTimer);
  if (step === timeStep) expireTimeSlots();
  bookingSteps.forEach((bookingStep) => {
    if (bookingStep) bookingStep.hidden = bookingStep !== step;
  });
  setReservationStatus('');
  if (step === timeStep && !document.hidden && !pendingBookingId) void refreshAvailableSlots();
}

function configureBookingTime(config: PublicTenantConfig): void {
  bookingTimeSlots = config.bookingTime.availableTimeSlots.filter(
    (slot, index, slots) => /^([01]\d|2[0-3]):[0-5]\d$/.test(slot) && slots.indexOf(slot) === index,
  );

  if (reservationTimeInput) {
    reservationTimeInput.replaceChildren(new Option('Выберите время', ''));
    bookingTimeSlots.forEach((slot) => {
      reservationTimeInput.add(new Option(slot, slot));
    });
    reservationTimeInput.disabled = bookingTimeSlots.length === 0;
  }

  if (bookingStartTime) {
    bookingStartTime.textContent = config.bookingTime.startTime;
    bookingStartTime.dateTime = config.bookingTime.startTime;
  }
  if (bookingEndTime) {
    bookingEndTime.textContent = config.bookingTime.endTime;
    bookingEndTime.dateTime = config.bookingTime.endTime;
  }

  if (bookingTimeHint) {
    bookingTimeHint.textContent = bookingTimeSlots.length > 0
      ? `Доступные интервалы с шагом ${config.bookingTime.slotDurationMinutes} мин.`
      : 'Для этой организации пока нет доступного времени';
  }

  updateTimeSlotAvailability();
}

function updateTimeSlotAvailability(): void {
  if (!reservationTimeInput) return;

  const selectedDate = reservationDateInput?.value ?? '';
  Array.from(reservationTimeInput.options).forEach((option) => {
    if (!option.value) return;
    option.disabled = Boolean(
      selectedDate && minimumDateTimeValue && `${selectedDate}T${option.value}` <= minimumDateTimeValue,
    );
  });

  if (reservationTimeInput.selectedOptions[0]?.disabled) {
    reservationTimeInput.value = '';
  }
}

function syncDateTimeValue(showMessage = false): boolean {
  if (!datetimeInput || !reservationDateInput || !reservationTimeInput) return true;

  const date = reservationDateInput.value;
  const time = reservationTimeInput.value;
  datetimeInput.value = date && time ? `${date}T${time}` : '';

  if (!date || !time) {
    if (showMessage) {
      setReservationStatus('Укажите дату и время в 24-часовом формате, например 18:30.', 'error');
      (date ? reservationTimeInput : reservationDateInput).focus();
    }
    return false;
  }

  return true;
}

function setFormBusy(isBusy: boolean) {
  submitting = isBusy;
  if (!submitButton) return;

  submitButton.disabled = isBusy || Boolean(pendingBookingId) || !selectedTable || !['free', 'limited'].includes(selectedTable.dataset.status ?? '');
  submitButton.textContent = isBusy ? 'Отправляем…' : pendingBookingId ? 'Ожидаем подтверждения…' : 'Забронировать';
}

function setReservationStatus(message: string, type: 'info' | 'success' | 'error' = 'info') {
  if (type === 'error') {
    showError(message);
    if (reservationStatus) reservationStatus.textContent = '';
    return;
  }
  if (!reservationStatus) return;

  reservationStatus.textContent = message;
  reservationStatus.className = `reservation-status ${type}`;
}

function validateSelectedTime(showMessage = false): boolean {
  if (!datetimeInput) return true;

  const selectedValue = datetimeInput.value.trim();
  if (!selectedValue) return true;

  if (minimumDateTimeValue && selectedValue <= minimumDateTimeValue) {
    if (showMessage) {
      setReservationStatus('Выберите будущее время.', 'error');
      reservationTimeInput?.focus();
    }
    return false;
  }

  const selectedTime = selectedValue.split('T')[1];
  if (!selectedTime || !bookingTimeSlots.includes(selectedTime)) {
    if (showMessage) {
      setReservationStatus('Выберите одно из доступных времён организации.', 'error');
      reservationTimeInput?.focus();
    }
    return false;
  }

  return true;
}

function getActiveFloorSection(): string {
  const activeTab = document.querySelector<HTMLButtonElement>('.floor-tab.active');
  const floor = activeTab?.dataset.floor;

  if (floor === 'vip1') return 'VIP 1';
  if (floor === 'vip2') return 'VIP 2';
  return 'Общий зал';
}

function formatReservationDateTime(value: string): string {
  const match = value.trim().match(/^(\d{2})[./](\d{2})[./](\d{4})\s+(\d{2}):(\d{2})/);
  if (!match) return value;

  const [, day, month, year, hour, minute] = match;
  return `${day}.${month}.${year} в ${hour}:${minute}`;
}

function resetReservationFormUi() {
  if (!reservationForm) return;

  reservationForm.reset();
  clearSelectedTable();
  void initRestaurantBooking();
}

async function submitReservation(payload: ReservationPayload) {
  if (pendingBookingId || submitting) return;
  setFormBusy(true);
  setReservationStatus('Отправляем бронь…', 'info');

  try {
    const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(payload)));
    const hash = Array.from(new Uint8Array(bytes), byte => byte.toString(16).padStart(2, '0')).join('');
    if (bookingAttempt && bookingAttempt.hash !== hash) {
      setReservationStatus('Предыдущий запрос ещё не проверен. Повторите его с прежними данными.', 'error');
      return;
    }
    bookingAttempt ??= { key: crypto.randomUUID(), hash };
    persistBookingAttempt();
    const response = await createReservation(payload, bookingAttempt!.key);
    if (response.status === 'Processing' && response.reservationId) {
      resumePendingBooking(response.reservationId);
      return;
    }
    if (response.success !== true) throw new Error('Бронь пока не подтверждена.');
    const successMessage = response.message || 'Бронирование успешно отправлено.';

    bookingConfirmed(successMessage);
  } catch (error) {
    if (error instanceof ApiError && [400, 409].includes(error.status) && error.code !== 'BOOKING_PROCESSING') {
      bookingAttempt = null;
      persistBookingAttempt();
    }
    if (error instanceof ApiError && error.status === 409 && error.code === 'EXISTING_RESERVATION') {
      setFormBusy(false);
      const when = error.existing?.scheduledAt
        ? ` на ${formatReservationDateTime(error.existing.scheduledAt)}`
        : '';
      setReservationStatus(`У вас уже есть актуальная бронь${when}. Нельзя оформить новую, пока действует текущая.`, 'error');
      return;
    }

    setReservationStatus('');
    reportError(error, 'Не удалось отправить бронь. Попробуйте ещё раз.');
    void refreshTableStatuses();
  } finally {
    setFormBusy(false);
  }
}

reservationForm?.addEventListener('submit', async (e) => {
  e.preventDefault();

  if (!reservationForm || submitting || pendingBookingId) return;

  if (!syncDateTimeValue(true)) {
    return;
  }

  const formData = new FormData(reservationForm);
  const payload: ReservationPayload = {
    customerName: String(formData.get('name') ?? '').trim(),
    customerPhone: toE164Digits(String(formData.get('phone') ?? '')),
    scheduledAt: String(formData.get('datetime') ?? '').trim(),
    tablesId: selectedTable?.dataset.id ?? '',
    remindBeforeHour: false,
    section: getActiveFloorSection(),
  };
  if (!payload.customerName || !payload.customerPhone || !payload.scheduledAt || !payload.tablesId) {
    setReservationStatus('Пожалуйста, заполните имя, телефон, время и выберите столик.', 'error');
    return;
  }

  if (!validateSelectedTime(true)) {
    return;
  }

  if (!isKazakhstanMobile(payload.customerPhone)) {
    setReservationStatus(KZ_PHONE_INVALID_MESSAGE, 'error');
    return;
  }

  await submitReservation(payload);
});

// Date chips are anchored to Kazakhstan calendar dates regardless of the
// visitor's device timezone. Slot strings remain timezone-free by API design.
function getAlmatyNowParts(): { year: number; month: number; day: number; hour: number; minute: number } {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Almaty',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).formatToParts(new Date());

  const map: Record<string, string> = {};
  parts.forEach((p) => {
    if (p.type !== 'literal') map[p.type] = p.value;
  });

  return {
    year: Number(map.year),
    month: Number(map.month),
    day: Number(map.day),
    hour: Number(map.hour),
    minute: Number(map.minute),
  };
}

function toDateValue(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
}

function resetAfterDateChange() {
  tableRequestRevision++;
  slotRequestRevision++;
  applyTableStatuses([]);
  if (datetimeInput) datetimeInput.value = '';
  clearSelectedTable();
}

function expireTimeSlots() {
  clearTimeout(slotExpiryTimer);
  timeOptions?.setAttribute('aria-busy', 'false');
  timeOptions?.querySelectorAll<HTMLButtonElement>('.time-option').forEach(option => { option.disabled = true; });
  if (continueToTableBtn) continueToTableBtn.disabled = true;
  if (slotStatus) slotStatus.textContent = 'Нет актуальных данных о свободном времени.';
}

function applyTimeSlots(slots: string[]) {
  if (!timeOptions) return;
  clearTimeout(slotExpiryTimer);
  timeOptions.setAttribute('aria-busy', 'false');
  if (datetimeInput?.value && !slots.includes(datetimeInput.value)) {
    datetimeInput.value = '';
    clearSelectedTable();
  }
  timeOptions.querySelectorAll<HTMLButtonElement>('.time-option').forEach(option => {
    if (!slots.includes(option.dataset.slot ?? '')) option.remove();
  });
  for (const slot of slots) {
    let option = Array.from(timeOptions.querySelectorAll<HTMLButtonElement>('.time-option')).find(item => item.dataset.slot === slot);
    if (!option) {
      option = document.createElement('button');
      option.type = 'button';
      option.className = 'time-option';
      option.textContent = slot.split('T')[1] ?? slot;
      option.dataset.slot = slot;
      option.addEventListener('click', () => {
        timeOptions.querySelectorAll<HTMLButtonElement>('.time-option').forEach(timeOption => {
          const isActive = timeOption.dataset.slot === slot;
          timeOption.classList.toggle('active', isActive);
          timeOption.setAttribute('aria-pressed', String(isActive));
        });
        if (datetimeInput) datetimeInput.value = slot;
        tableRequestRevision++;
        applyTableStatuses([]);
        clearSelectedTable();
        if (continueToTableBtn) continueToTableBtn.disabled = false;
        continueToTableBtn?.focus();
      });
      const following = Array.from(timeOptions.children).find(child => (child as HTMLElement).dataset.slot! > slot);
      timeOptions.insertBefore(option, following ?? null);
    }
    option.disabled = false;
    option.classList.toggle('active', datetimeInput?.value === slot);
    option.setAttribute('aria-pressed', String(datetimeInput?.value === slot));
  }
  if (continueToTableBtn) continueToTableBtn.disabled = !datetimeInput?.value;
  if (slotStatus) slotStatus.textContent = slots.length ? '' : 'На эту дату свободного времени нет. Выберите другой день.';
  slotExpiryTimer = setTimeout(expireTimeSlots, 30_000);
}

async function selectBookingDate(_date: string, button: HTMLButtonElement) {
  clearTimeout(slotExpiryTimer);
  dateOptions?.querySelectorAll<HTMLButtonElement>('.date-option').forEach((option) => {
    const isActive = option === button;
    option.classList.toggle('active', isActive);
    option.setAttribute('aria-pressed', String(isActive));
  });

  resetAfterDateChange();
  if (continueToTableBtn) continueToTableBtn.disabled = true;
  showBookingStep(timeStep);
  if (timeOptions) {
    timeOptions.innerHTML = '';
    timeOptions.setAttribute('aria-busy', 'true');
  }
  if (slotStatus) slotStatus.textContent = 'Загружаем свободное время…';
}

async function initRestaurantBooking() {
  if (!dateOptions || !timeOptions || !datetimeInput) return;

  dateOptions.innerHTML = '';
  timeOptions.innerHTML = '';
  resetAfterDateChange();
  if (continueToTimeBtn) continueToTimeBtn.disabled = true;
  if (continueToTableBtn) continueToTableBtn.disabled = true;
  showBookingStep(dateStep);

  const almaty = getAlmatyNowParts();
  const today = new Date(Date.UTC(almaty.year, almaty.month - 1, almaty.day));
  for (let offset = 0; offset < 7; offset++) {
    const date = new Date(today);
    date.setUTCDate(today.getUTCDate() + offset);

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'date-option';
    button.dataset.date = toDateValue(date);
    button.setAttribute('aria-pressed', 'false');

    const day = document.createElement('span');
    day.className = 'date-option-day';
    day.textContent = offset === 0
      ? 'Сегодня'
      : new Intl.DateTimeFormat('ru-RU', { weekday: 'short', timeZone: 'UTC' }).format(date);

    const label = document.createElement('span');
    label.className = 'date-option-date';
    label.textContent = new Intl.DateTimeFormat('ru-RU', {
      day: 'numeric',
      month: 'short',
      timeZone: 'UTC',
    }).format(date);

    button.append(day, label);
    button.addEventListener('click', () => {
      dateOptions.querySelectorAll<HTMLButtonElement>('.date-option').forEach((dateOption) => {
        const isActive = dateOption === button;
        dateOption.classList.toggle('active', isActive);
        dateOption.setAttribute('aria-pressed', String(isActive));
      });
      if (continueToTimeBtn) continueToTimeBtn.disabled = false;
      continueToTimeBtn?.focus();
    });
    dateOptions.appendChild(button);
  }
}

continueToTimeBtn?.addEventListener('click', () => {
  const selectedDate = dateOptions?.querySelector<HTMLButtonElement>('.date-option.active');
  if (!selectedDate?.dataset.date) return;
  void selectBookingDate(selectedDate.dataset.date, selectedDate);
});

continueToTableBtn?.addEventListener('click', () => {
  if (!datetimeInput?.value || continueToTableBtn.disabled) return;
  showBookingStep(tableStep);
  openTablePickerBtn?.focus();
});

continueToNameBtn?.addEventListener('click', () => {
  if (!selectedTable) return;
  const resumeAtPhone = Boolean(nameInput?.value.trim() && phoneInput?.value.trim());
  showBookingStep(resumeAtPhone ? phoneStep : nameStep);
  (resumeAtPhone ? phoneInput : nameInput)?.focus();
});

function continueToPhone() {
  if (!nameInput?.value.trim()) {
    setReservationStatus('Введите имя, чтобы продолжить.', 'error');
    return;
  }

  showBookingStep(phoneStep);
  phoneInput?.focus();
}

continueToPhoneBtn?.addEventListener('click', continueToPhone);
nameInput?.addEventListener('keydown', (event) => {
  if (event.key !== 'Enter') return;
  event.preventDefault();
  continueToPhone();
});

document.querySelectorAll<HTMLButtonElement>('[data-booking-back]').forEach((button) => {
  button.addEventListener('click', () => {
    switch (button.dataset.bookingBack) {
      case 'date':
        clearTimeout(slotExpiryTimer);
        resetAfterDateChange();
        showBookingStep(dateStep);
        dateOptions?.querySelector<HTMLButtonElement>('.date-option.active')?.focus();
        break;
      case 'time':
        clearSelectedTable();
        showBookingStep(timeStep);
        timeOptions?.querySelector<HTMLButtonElement>('.time-option.active')?.focus();
        break;
      case 'table':
        showBookingStep(tableStep);
        openTablePickerBtn?.focus();
        break;
      case 'name':
        showBookingStep(nameStep);
        nameInput?.focus();
        break;
    }
  });
});

// переключение вкладок между залами 

const floorTabs = document.querySelectorAll<HTMLButtonElement>('.floor-tab');
const floorScenes = document.querySelectorAll<HTMLElement>('.floor-plan-scene');

floorTabs.forEach((tab) => {
  tab.addEventListener('click', () => {
    const targetFloor = tab.dataset.floor;

    floorTabs.forEach((t) => t.classList.remove('active'));
    tab.classList.add('active');

    floorScenes.forEach((scene) => {
      scene.classList.toggle('active', scene.dataset.scene === targetFloor);
    });

    // Сбрасываем только положение карты; выбранный стол сменится следующим кликом.
    scale = 1;
    centerActiveScene();
    applyTransform();
  });
});

successModalCloseBtn?.addEventListener('click', () => {
  if (successModalOverlay) successModalOverlay.hidden = true;
});

successModalOverlay?.addEventListener('click', (e) => {
  if (e.target === successModalOverlay) successModalOverlay.hidden = true;
});
