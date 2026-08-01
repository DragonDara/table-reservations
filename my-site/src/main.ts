import './style.css';
import {
  createReservation,
  getTables,
  type ReservationPayload,
  type TableAvailability,
} from './api';

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
const toggleSidebarBtn = document.getElementById('toggleSidebarBtn') as HTMLButtonElement | null;
const tableSidebar = document.querySelector<HTMLElement>('.table-sidebar');
const confirmTableBtn = document.getElementById('confirmTableBtn') as HTMLButtonElement | null;
const clearSelectionBtn = document.getElementById('clearSelectionBtn') as HTMLButtonElement | null;
const successModalOverlay = document.getElementById('successModalOverlay') as HTMLElement | null;
const successModalText = document.getElementById('successModalText') as HTMLElement | null;
const successModalCloseBtn = document.getElementById('successModalCloseBtn') as HTMLButtonElement | null;
const selectedSummary = document.getElementById('selectedSummary') as HTMLElement | null;
const selectedTablesList = document.getElementById('selectedTablesList') as HTMLElement | null;
const selectedTotal = document.getElementById('selectedTotal') as HTMLElement | null;

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

viewport?.addEventListener('touchstart', (e) => {
  if (e.touches.length === 1) {
    isDragging = true;
    dragStartX = e.touches[0].clientX - panX;
    dragStartY = e.touches[0].clientY - panY;
  } else if (e.touches.length === 2) {
    isDragging = false;
    pinchStartDist = getTouchDistance(e.touches);
    pinchStartScale = scale;
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
    setScale(pinchStartScale * (dist / pinchStartDist));
  }
}, { passive: false });

viewport?.addEventListener('touchend', () => {
  isDragging = false;
});

// открытие / закрытие модалки
openTablePickerBtn?.addEventListener('click', () => {
  if (overlay) overlay.hidden = false;
  scale = 1;
  panX = 0;
  panY = 0;
  applyTransform();
  centerCanvas();
});

function closeTableModal() {
  if (!overlay) return;

  selectedTables.forEach((marker) => marker.classList.remove('selected'));
  selectedTables.clear();
  updateSummary();

  if (selectedTableBadge && tablePlaceholder) {
    selectedTableBadge.hidden = true;
    selectedTableBadge.textContent = '';
    tablePlaceholder.hidden = false;
  }

  overlay.hidden = true;
}

closeModalBtn?.addEventListener('click', closeTableModal);
mobileCloseModalBtn?.addEventListener('click', closeTableModal);

overlay?.addEventListener('click', (e) => {
  if (e.target === overlay) closeTableModal();
});

toggleSidebarBtn?.addEventListener('click', () => {
  if (!tableSidebar) return;

  const isCollapsed = tableSidebar.classList.toggle('collapsed');
  toggleSidebarBtn.textContent = isCollapsed ? '▴' : '▾';
  toggleSidebarBtn.setAttribute('aria-expanded', String(!isCollapsed));
  toggleSidebarBtn.setAttribute('aria-label', isCollapsed ? 'Развернуть панель' : 'Свернуть панель');
});

// выбор столика
// статусы столиков: занято скоро / занято с ограничением / свободно ---

const THRESHOLD_HOURS = 2; // если до следующей брони меньше — стол недоступен вообще

const tablePopup = document.getElementById('tablePopup') as HTMLElement | null;
const tablePopupIcon = document.getElementById('tablePopupIcon') as HTMLElement | null;
const tablePopupTitle = document.getElementById('tablePopupTitle') as HTMLElement | null;
const tablePopupText = document.getElementById('tablePopupText') as HTMLElement | null;
const tablePopupActions = document.getElementById('tablePopupActions') as HTMLElement | null;

function closeTablePopup() {
  if (tablePopup) tablePopup.hidden = true;
}

function formatHoursFromNow(hours: number): string {
  const target = new Date(Date.now() + hours * 60 * 60 * 1000);
  return target.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
}

function formatPhoneNumber(rawValue: string): string {
  let digits = rawValue.replace(/\D/g, '');

  // человек часто сам вбивает код страны (7 или 8) — убираем, он и так будет "+7"
  if (digits.startsWith('7') || digits.startsWith('8')) {
    digits = digits.slice(1);
  }

  digits = digits.slice(0, 10); // максимум 10 цифр после кода страны

  const code = digits.slice(0, 3);
  const part1 = digits.slice(3, 6);
  const part2 = digits.slice(6, 8);
  const part3 = digits.slice(8, 10);

  let result = '+7';
  if (code) result += ` ${code}`;
  if (part1) result += ` (${part1})`;
  if (part2) result += ` ${part2}`;
  if (part3) result += ` ${part3}`;

  return result;
}

const phoneInput = document.getElementById('phone') as HTMLInputElement | null;

phoneInput?.addEventListener('input', () => {
  const cursorWasAtEnd = phoneInput.selectionEnd === phoneInput.value.length;
  phoneInput.value = formatPhoneNumber(phoneInput.value);
  if (cursorWasAtEnd) {
    phoneInput.setSelectionRange(phoneInput.value.length, phoneInput.value.length);
  }
});

const selectedTables = new Set<HTMLButtonElement>();

function updateSummary() {
  if (!selectedSummary || !selectedTablesList || !selectedTotal) return;

  if (selectedTables.size === 0) {
    selectedSummary.hidden = true;
    if (confirmTableBtn) confirmTableBtn.disabled = true;
    return;
  }

  selectedSummary.hidden = false;
  if (confirmTableBtn) confirmTableBtn.disabled = false;

  selectedTablesList.innerHTML = '';
  let totalSeats = 0;

  selectedTables.forEach((marker) => {
    const id = marker.dataset.id;
    const seats = Number(marker.dataset.seats ?? 0);
    const extraSeat = marker.dataset.extraSeat === '1' ? '+1' : '';
    totalSeats += seats;

    const chip = document.createElement('div');
    chip.className = 'selected-chip';

    const label = document.createElement('span');
    const isVip = marker.classList.contains('vip-table');
    label.textContent = isVip
    ? `VIP ${id} · ${seats}${extraSeat} мест`
      : `${id} столик · ${seats}${extraSeat} мест`;

    const removeBtn = document.createElement('button');
    removeBtn.type = 'button';
    removeBtn.className = 'chip-remove';
    removeBtn.setAttribute('aria-label', 'Убрать столик');
    removeBtn.textContent = '✕';
    removeBtn.addEventListener('click', () => deselectTable(marker));

    chip.appendChild(label);
    chip.appendChild(removeBtn);
    selectedTablesList.appendChild(chip);
  });

  selectedTotal.textContent = `Итого: ${selectedTables.size} столов. · ${totalSeats} мест`;
}

function selectTable(marker: HTMLButtonElement) {
  selectedTables.add(marker);
  marker.classList.add('selected');
  updateSummary();
}

function deselectTable(marker: HTMLButtonElement) {
  selectedTables.delete(marker);
  marker.classList.remove('selected');
  updateSummary();
}

function toggleTable(marker: HTMLButtonElement) {
  if (selectedTables.has(marker)) {
    deselectTable(marker);
  } else {
    selectTable(marker);
  }
}

function showBlockedPopup(marker?: HTMLButtonElement) {
  if (!tablePopup || !tablePopupIcon || !tablePopupTitle || !tablePopupText || !tablePopupActions) return;

  const nextReservationHours = marker?.dataset.nextReservationHours
    ? parseFloat(marker.dataset.nextReservationHours)
    : null;

  tablePopupIcon.textContent = '✕';
  tablePopupIcon.className = 'table-popup-icon icon-blocked';
  tablePopupTitle.textContent = 'Столик уже занят';

  if (nextReservationHours !== null) {
    const bookedAt = formatHoursFromNow(nextReservationHours);
    tablePopupText.textContent = `Этот столик уже забронирован другими гостями. Бронь уже занята на ${bookedAt}. Выберите другой столик или другое время.`;
  } else {
    tablePopupText.textContent = 'Этот столик уже забронирован другими гостями. Выберите другой столик или другое время.';
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

async function refreshTableStatuses() {
  let tables: TableAvailability[];
  try {
    tables = await getTables();
  } catch (err) {
    console.error('Не удалось загрузить статусы столиков', err);
    return;
  }

  const byId = new Map(tables.map((t) => [String(t.id), t]));

  tableMarkers.forEach((marker) => {
  const id = marker.dataset.id;
  const info = id ? byId.get(id) : undefined;
  if (!info) return;

  let status = info.status;

  // если до брони меньше порога — бронировать всё равно нельзя, значит визуально это "занято", а не "с предупреждением"
  if (status === 'limited' && info.nextReservationHours != null && info.nextReservationHours < THRESHOLD_HOURS) {
    status = 'occupied';
  }

  marker.dataset.status = status;

  if (info.nextReservationHours != null) {
    marker.dataset.nextReservationHours = String(info.nextReservationHours);
  } else {
    delete marker.dataset.nextReservationHours;
  }
});
}

tableMarkers.forEach((marker) => {
  marker.addEventListener('click', () => {
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

    toggleTable(marker);
  });
});

refreshTableStatuses();

clearSelectionBtn?.addEventListener('click', () => {
  selectedTables.forEach((marker) => marker.classList.remove('selected'));
  selectedTables.clear();
  updateSummary();
});

confirmTableBtn?.addEventListener('click', () => {
  if (selectedTables.size === 0) return;

  const markers = Array.from(selectedTables);
  const labels = markers.map((m) =>
    m.classList.contains('vip-table') ? `VIP №${m.dataset.id}` : `№${m.dataset.id}`
  );

  if (selectedTableBadge && tablePlaceholder) {
    selectedTableBadge.textContent = labels.length === 1
      ? `Столик ${labels[0]}`
      : `Столики ${labels.join(', ')}`;
    selectedTableBadge.hidden = false;
    tablePlaceholder.hidden = true;
  }

  if (overlay) overlay.hidden = true;
});

const reservationForm = document.getElementById('reservationForm') as HTMLFormElement | null;
const submitButton = reservationForm?.querySelector<HTMLButtonElement>('button[type="submit"]') ?? null;
const reservationStatus = document.getElementById('reservationStatus') as HTMLParagraphElement | null;

function setFormBusy(isBusy: boolean) {
  if (!submitButton) return;

  submitButton.disabled = isBusy;
  submitButton.textContent = isBusy ? 'Отправляем…' : 'Забронировать';
}

function setReservationStatus(message: string, type: 'info' | 'success' | 'error' = 'info') {
  if (!reservationStatus) return;

  reservationStatus.textContent = message;
  reservationStatus.className = `reservation-status ${type}`;
}

function getActiveFloorSection(): string {
  const activeTab = document.querySelector<HTMLButtonElement>('.floor-tab.active');
  const floor = activeTab?.dataset.floor;

  if (floor === 'vip1') return 'VIP 1';
  if (floor === 'vip2') return 'VIP 2';
  return 'Общий зал';
}


reservationForm?.addEventListener('submit', async (e) => {
  e.preventDefault();

  if (!reservationForm) return;

  const formData = new FormData(reservationForm);
  const selectedIds = Array.from(selectedTables).map((marker) => marker.dataset.id ?? '');
const payload: ReservationPayload = {
  customerName: String(formData.get('name') ?? '').trim(),
  customerPhone: `+${String(formData.get('phone') ?? '').replace(/\D/g, '')}`,
  scheduledAt: String(formData.get('datetime') ?? '').trim(),
  tablesId: selectedIds.filter(Boolean).join(','),
  remindBeforeHour: formData.get('remind') === 'on' ? 1 : 0,
  section: getActiveFloorSection(),
};
  if (!payload.customerName || !payload.customerPhone || !payload.scheduledAt || !payload.tablesId) {
    setReservationStatus('Пожалуйста, заполните имя, телефон, время и выберите столик.', 'error');
    return;
  }

  setFormBusy(true);
  setReservationStatus('Отправляем бронь…', 'info');

  try {
    const response = await createReservation(payload);
    const successMessage = response.message || response.reservationId
      ? `Бронирование отправлено${response.reservationId ? ` (ID: ${response.reservationId})` : ''}.`
      : 'Бронирование успешно отправлено.';

    setReservationStatus(successMessage, 'success');

    if (successModalOverlay && successModalText) {
      successModalText.textContent = successMessage;
      successModalOverlay.hidden = false;
    }

    refreshTableStatuses();
    reservationForm.reset();
    selectedTables.forEach((marker) => marker.classList.remove('selected'));
    selectedTables.clear();
    updateSummary();

    if (selectedTableBadge && tablePlaceholder) {
      selectedTableBadge.hidden = true;
      selectedTableBadge.textContent = '';
      tablePlaceholder.hidden = false;
    }
  } catch (error) {
    console.error('Reservation submission failed', error);
    const message = error instanceof Error && error.message
      ? error.message
      : 'Не удалось отправить бронь. Проверьте подключение к API или попробуйте позже.';

    setReservationStatus(message, 'error');
  } finally {
    setFormBusy(false);
  }
});

const datetimeInput = document.getElementById('datetime') as HTMLInputElement | null;

if (datetimeInput) {
  const now = new Date();
  now.setMinutes(now.getMinutes() + 5 - now.getTimezoneOffset()); // +5 мин минимальный лид-тайм, компенсация часового пояса
  datetimeInput.min = now.toISOString().slice(0, 16); // формат YYYY-MM-DDTHH:mm
}

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

    // сброс зума/пана и выбора при смене зала
    scale = 1;
    centerActiveScene();
    applyTransform();

    selectedTables.forEach((marker) => marker.classList.remove('selected'));
    selectedTables.clear();
    updateSummary();
  });
});

successModalCloseBtn?.addEventListener('click', () => {
  if (successModalOverlay) successModalOverlay.hidden = true;
});

successModalOverlay?.addEventListener('click', (e) => {
  if (e.target === successModalOverlay) successModalOverlay.hidden = true;
});