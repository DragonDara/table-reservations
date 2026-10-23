import { veilAreas, type VeilArea, type VeilTable } from './veil-floor-plan-data';
import { veilHallDiagram } from './veil-floor-plan-diagram';
import './veil-floor-plan.css';
import type { TableAvailability } from '../api';

interface Selection { area: VeilArea; table: VeilTable }
export interface VeilBookingSelection { tableId: string; section: string }
export interface VeilFloorPlanController {
  setAvailability(tables: TableAvailability[]): void;
  clearSelection(): void;
  getSelection(): VeilBookingSelection | null;
}
interface PickerOptions {
  bookingEnabled?: boolean;
  durationMinutes?: number;
  beforeOpen?: () => Promise<boolean>;
  onSelect?: (selection: VeilBookingSelection | null) => void;
}

function tableKind(table: VeilTable): string {
  return table.shape === 'round' ? 'Малый круглый' : table.shape === 'rectangular' ? 'Большой' : '';
}

function tableLabel(area: VeilArea, table: VeilTable): string {
  return area.id === 'vip' ? `VIP, комната №${table.number} целиком`
    : `${area.label}, ${tableKind(table).toLowerCase()} столик №${table.number}`.replace('  ', ' ');
}

/** Venue artwork with API-backed availability, or local selection in preview mode. */
export function initVeilFloorPlan(options: PickerOptions = {}): VeilFloorPlanController | null {
  const opener = document.querySelector<HTMLButtonElement>('#openTablePicker');
  const oldOverlay = document.getElementById('tableModalOverlay');
  if (!opener || !oldOverlay || document.getElementById('veilTableDialog')) return null;

  // The shared restaurant module must not fetch TheTochka's inventory here.
  opener.dataset.floorPlan = 'veil';
  oldOverlay.remove();

  const dialog = document.createElement('dialog');
  dialog.id = 'veilTableDialog';
  dialog.className = 'veil-dialog';
  dialog.setAttribute('aria-labelledby', 'veilPickerTitle');
  dialog.setAttribute('aria-describedby', 'veilPickerNotice');
  dialog.innerHTML = `
    <div class="veil-picker">
      <header class="veil-picker-header">
        <div><p class="veil-picker-eyebrow">The Veil · схема заведения</p>
          <h2 id="veilPickerTitle">Выберите столик</h2></div>
        <button type="button" class="veil-icon-button" data-close aria-label="Закрыть карту">✕</button>
      </header>
      <div class="veil-area-tabs" role="tablist" aria-label="Зона ресторана">
        ${veilAreas.map((area, index) => `
          <button type="button" role="tab" id="veil-tab-${area.id}" data-area="${area.id}"
            aria-controls="veilAreaPanel" aria-selected="${index === 0}" tabindex="${index === 0 ? 0 : -1}">
            ${area.label}<span>${area.tables.length}</span>
          </button>`).join('')}
      </div>
      <div class="veil-picker-body" id="veilAreaPanel" role="tabpanel" aria-labelledby="veil-tab-hall">
        <div class="veil-map-section">
          <div class="veil-map-toolbar">
            <p>Нажмите на столик на схеме</p>
            <div class="veil-map-tools" role="group" aria-label="Масштаб карты">
              <button type="button" class="veil-icon-button" data-zoom-out aria-label="Уменьшить карту">−</button>
              <button type="button" class="veil-fit-button" data-fit aria-label="Сбросить масштаб карты">По размеру</button>
              <button type="button" class="veil-icon-button" data-zoom-in aria-label="Увеличить карту">+</button>
            </div>
          </div>
          <div class="veil-map-viewport" tabindex="0" role="group" aria-label="Схема столов. Прокрутите для перемещения.">
            <div class="veil-map-stage"><div class="veil-map"></div></div>
          </div>
          <p class="veil-map-hint">Перемещайте схему, чтобы рассмотреть столики</p>
        </div>
        <aside class="veil-table-list">
          <h3>Столики <span data-area-label>в зале</span></h3>
          <div class="veil-table-shapes" hidden>
            <span><i class="veil-shape-round" aria-hidden="true"></i>Малый круглый</span>
            <span><i class="veil-shape-large" aria-hidden="true"></i>Большой</span>
          </div>
          <div class="veil-table-grid" role="group" aria-label="Выбор столика по номеру"></div>
          <p class="veil-capacity-note">Вместимость столиков уточняется</p>
          <p class="veil-image-error" hidden>Схема не загрузилась. Выберите столик по номеру.</p>
        </aside>
      </div>
      <footer class="veil-picker-footer">
        <div class="veil-selection" role="status" aria-live="polite">
          <strong data-selection-title>Столик пока не выбран</strong>
          <span data-selection-detail>Выберите зону и удобное место</span>
        </div>
        <button type="button" class="btn btn-primary" data-confirm disabled>Выбрать столик</button>
        <p id="veilPickerNotice">Онлайн-бронирование скоро откроется. Сейчас можно выбрать столик на схеме.</p>
      </footer>
    </div>`;
  document.querySelector('[data-app-root]')?.append(dialog);

  const viewport = dialog.querySelector<HTMLElement>('.veil-map-viewport')!;
  const stage = dialog.querySelector<HTMLElement>('.veil-map-stage')!;
  const map = dialog.querySelector<HTMLElement>('.veil-map')!;
  const grid = dialog.querySelector<HTMLElement>('.veil-table-grid')!;
  const tabs = [...dialog.querySelectorAll<HTMLButtonElement>('[role="tab"]')];
  const confirm = dialog.querySelector<HTMLButtonElement>('[data-confirm]')!;
  const zoomOut = dialog.querySelector<HTMLButtonElement>('[data-zoom-out]')!;
  const zoomIn = dialog.querySelector<HTMLButtonElement>('[data-zoom-in]')!;
  let activeArea = veilAreas[0];
  let confirmed: Selection | null = null;
  let draft: Selection | null = null;
  let zoom = 1;
  let previousOverflow = '';
  let availability = new Map<number, TableAvailability>();
  let didConfirm = false;

  const selectionValue = (): VeilBookingSelection | null => confirmed ? {
    tableId: String(confirmed.table.number),
    section: confirmed.area.id === 'vip' ? `VIP №${confirmed.table.number}, комната целиком` : 'Общий зал',
  } : null;
  const selectable = (table: VeilTable): boolean => !options.bookingEnabled
    || (availability.get(table.number)?.seats ?? 0) > 0
      && ['free', 'limited'].includes(availability.get(table.number)?.status ?? '');

  if (options.bookingEnabled) {
    dialog.querySelector('#veilPickerNotice')!.textContent = `Бронь на ${options.durationMinutes ?? 60} мин. VIP-комната бронируется целиком. Занятые места недоступны для выбора.`;
  }

  function updateAvailability(): void {
    dialog.querySelectorAll<HTMLButtonElement>('[data-table]').forEach((button) => {
      const table = activeArea.tables.find((table) => table.id === button.dataset.table);
      button.disabled = !table || !selectable(table);
      button.title = button.disabled ? 'Недоступно на выбранное время' : '';
    });
  }

  function clearSelection(): void {
    confirmed = null;
    draft = null;
    availability.clear();
    const badge = document.getElementById('selectedTableBadge');
    if (badge) { badge.textContent = ''; badge.hidden = true; }
    const placeholder = document.querySelector<HTMLElement>('.table-picker-placeholder');
    if (placeholder) placeholder.hidden = false;
    updateAvailability();
    updateSelection();
  }

  function resizeMap(): void {
    const fitWidth = Math.min(viewport.clientWidth - 32, (viewport.clientHeight - 32) * activeArea.width / activeArea.height);
    const width = Math.max(activeArea.minMapWidth, fitWidth) * zoom;
    map.style.width = `${width}px`;
    map.style.setProperty('--veil-plan-number-size', `${Math.max(13, Math.min(20, width * .022))}px`);
    map.style.setProperty('--veil-plan-annotation-size', `${Math.max(11, Math.min(16, width * .018))}px`);
    map.style.setProperty('--veil-plan-caption-size', `${Math.max(10, Math.min(14, width * .017))}px`);
    map.style.setProperty('--veil-plan-caption-width', `${Math.max(104, Math.min(220, width * .28))}px`);
    map.style.aspectRatio = `${activeArea.width} / ${activeArea.height}`;
    stage.style.minWidth = `${width + 32}px`;
    stage.style.minHeight = `${width * activeArea.height / activeArea.width + 32}px`;
    zoomOut.disabled = zoom <= 1;
    zoomIn.disabled = zoom >= 3;
  }

  function updateSelection(): void {
    dialog.querySelectorAll<HTMLButtonElement>('[data-table]').forEach((button) => {
      button.setAttribute('aria-pressed', String(button.dataset.table === draft?.table.id));
    });
    dialog.querySelector('[data-selection-title]')!.textContent = draft
      ? draft.area.id === 'vip' ? `VIP-комната №${draft.table.number}` : `Столик №${draft.table.number}` : 'Столик пока не выбран';
    dialog.querySelector('[data-selection-detail]')!.textContent = draft
      ? [draft.area.label, draft.area.id === 'vip' ? 'Комната целиком' : tableKind(draft.table), draft.table.capacity === null ? 'Вместимость уточняется' : `До ${draft.table.capacity} гостей`].filter(Boolean).join(' · ')
      : 'Выберите зону и удобное место';
    confirm.disabled = !draft || !selectable(draft.table);
  }

  function revealTable(id: string): void {
    const marker = map.querySelector<HTMLElement>(`[data-table="${id}"]`);
    if (!marker) return;
    viewport.scrollTo({
      left: map.offsetLeft + marker.offsetLeft - viewport.clientWidth / 2,
      top: map.offsetTop + marker.offsetTop - viewport.clientHeight / 2,
      behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth',
    });
  }

  function renderArea(area: VeilArea): void {
    activeArea = area;
    map.dataset.rendering = area.image ? 'image' : 'diagram';
    zoom = 1;
    dialog.querySelector('#veilAreaPanel')!.setAttribute('aria-labelledby', `veil-tab-${area.id}`);
    dialog.querySelector('[data-area-label]')!.textContent = `· ${area.label}`;
    dialog.querySelector('.veil-map-hint')!.textContent = area.description;
    dialog.querySelector('.veil-capacity-note')!.textContent = area.tables.some((table) => table.capacity === null)
      ? 'Вместимость столиков уточняется' : 'Указана максимальная вместимость столиков';
    dialog.querySelector<HTMLElement>('.veil-table-shapes')!.hidden = !area.tables.some((table) => table.shape);
    dialog.querySelector<HTMLElement>('.veil-image-error')!.hidden = true;
    tabs.forEach((tab) => {
      const selected = tab.dataset.area === area.id;
      tab.setAttribute('aria-selected', String(selected));
      tab.tabIndex = selected ? 0 : -1;
    });
    map.innerHTML = `${area.image
      ? `<img src="${area.image}" width="${area.width}" height="${area.height}" alt="" draggable="false" />`
      : veilHallDiagram(area)}
      ${(area.landmarks ?? []).map((landmark) => `
        <span class="veil-map-landmark veil-landmark-${landmark.kind}"
          style="left:${landmark.x}%;top:${landmark.y}%;width:${landmark.width}%;height:${landmark.height}%"><span>${landmark.label}</span></span>`).join('')}
      ${area.tables.map((table) => `
        <button type="button" class="veil-map-table" data-table="${table.id}" data-shape="${table.shape ?? ''}"
          style="left:${table.x}%;top:${table.y}%;--veil-table-width:${table.width ?? 0}%;--veil-table-height:${table.height ?? 0}%" aria-pressed="false"
          aria-label="${tableLabel(area, table)}">${table.number}</button>`).join('')}`;
    map.querySelector('img')?.addEventListener('error', () => {
      dialog.querySelector<HTMLElement>('.veil-image-error')!.hidden = false;
    });
    grid.innerHTML = area.tables.map((table) => `
      <button type="button" data-table="${table.id}" aria-pressed="false"
        aria-label="${tableLabel(area, table)}">${table.number}</button>`).join('');
    resizeMap();
    viewport.scrollTo(0, 0);
    updateAvailability();
    updateSelection();
  }

  function changeZoom(next: number): void {
    const centerX = (viewport.scrollLeft + viewport.clientWidth / 2 - map.offsetLeft) / map.offsetWidth;
    const centerY = (viewport.scrollTop + viewport.clientHeight / 2 - map.offsetTop) / map.offsetHeight;
    zoom = Math.max(1, Math.min(3, next));
    resizeMap();
    viewport.scrollTo(map.offsetLeft + centerX * map.offsetWidth - viewport.clientWidth / 2,
      map.offsetTop + centerY * map.offsetHeight - viewport.clientHeight / 2);
  }

  opener.addEventListener('click', async () => {
    if (options.beforeOpen) {
      opener.disabled = true;
      opener.setAttribute('aria-busy', 'true');
      try { if (!await options.beforeOpen()) return; }
      finally { opener.disabled = false; opener.removeAttribute('aria-busy'); }
    }
    didConfirm = false;
    draft = confirmed;
    previousOverflow = document.documentElement.style.overflow;
    document.documentElement.style.overflow = 'hidden';
    dialog.showModal();
    renderArea(confirmed?.area ?? activeArea);
    if (confirmed) revealTable(confirmed.table.id);
    tabs.find((tab) => tab.dataset.area === activeArea.id)?.focus();
  });

  dialog.querySelector('[data-close]')!.addEventListener('click', () => dialog.close());
  dialog.addEventListener('close', () => {
    document.documentElement.style.overflow = previousOverflow;
    opener.focus({ preventScroll: true });
    if (didConfirm) options.onSelect?.(selectionValue());
  });
  dialog.addEventListener('click', (event) => {
    if (event.target !== dialog) return;
    const rect = dialog.getBoundingClientRect();
    if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) dialog.close();
  });

  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => renderArea(veilAreas.find((area) => area.id === tab.dataset.area)!));
    tab.addEventListener('keydown', (event) => {
      let next: number;
      if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
      else if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
      else if (event.key === 'Home') next = 0;
      else if (event.key === 'End') next = tabs.length - 1;
      else return;
      event.preventDefault();
      tabs[next].click();
      tabs[next].focus();
    });
  });

  for (const container of [map, grid]) {
    container.addEventListener('click', (event) => {
      const button = (event.target as HTMLElement).closest<HTMLButtonElement>('[data-table]');
      const table = activeArea.tables.find((item) => item.id === button?.dataset.table);
      if (!table || !selectable(table)) return;
      draft = { area: activeArea, table };
      updateSelection();
      if (container === grid) revealTable(table.id);
    });
  }

  confirm.addEventListener('click', () => {
    if (!draft || !selectable(draft.table)) return;
    confirmed = draft;
    didConfirm = true;
    const badge = document.getElementById('selectedTableBadge');
    const placeholder = document.querySelector<HTMLElement>('.table-picker-placeholder');
    if (badge) {
      badge.textContent = confirmed.area.id === 'vip' ? `VIP · Комната №${confirmed.table.number} целиком`
        : `${confirmed.area.label} · Столик №${confirmed.table.number}`;
      badge.hidden = false;
    }
    if (placeholder) placeholder.hidden = true;
    dialog.close();
  });

  zoomIn.addEventListener('click', () => changeZoom(zoom + 0.5));
  zoomOut.addEventListener('click', () => changeZoom(zoom - 0.5));
  dialog.querySelector('[data-fit]')!.addEventListener('click', () => {
    zoom = 1;
    resizeMap();
    viewport.scrollTo(0, 0);
  });

  // Native scrolling supports touch and trackpads; mouse users can also drag.
  let drag: { x: number; y: number; left: number; top: number } | null = null;
  viewport.addEventListener('pointerdown', (event) => {
    if (event.pointerType !== 'mouse' || event.button !== 0 || (event.target as HTMLElement).closest('button')) return;
    drag = { x: event.clientX, y: event.clientY, left: viewport.scrollLeft, top: viewport.scrollTop };
    viewport.setPointerCapture(event.pointerId);
    viewport.classList.add('is-dragging');
    event.preventDefault();
  });
  viewport.addEventListener('pointermove', (event) => {
    if (!drag) return;
    viewport.scrollTo(drag.left + drag.x - event.clientX, drag.top + drag.y - event.clientY);
  });
  viewport.addEventListener('lostpointercapture', () => {
    drag = null;
    viewport.classList.remove('is-dragging');
  });
  new ResizeObserver(() => { if (dialog.open) resizeMap(); }).observe(viewport);
  return {
    clearSelection,
    getSelection: selectionValue,
    setAvailability(tables) {
      availability = new Map(tables.map((table) => [table.id, table]));
      for (const area of veilAreas) for (const table of area.tables) {
        const current = availability.get(table.number);
        if (current && current.seats > 0) table.capacity = current.seats;
      }
      if (confirmed && !selectable(confirmed.table)) {
        confirmed = null;
        draft = null;
        options.onSelect?.(null);
      }
      updateAvailability();
      updateSelection();
    },
  };
}

export function showVeilFloorPlanPreview(): void {
  const dateStep = document.getElementById('dateStep');
  const tableStep = document.getElementById('tableStep');
  const opener = document.querySelector<HTMLButtonElement>('#openTablePicker');
  if (!tableStep || !opener) return;
  if (dateStep) dateStep.hidden = true;
  tableStep.hidden = false;
  tableStep.querySelectorAll<HTMLElement>('.booking-step-number, #continueToName, [data-booking-back]').forEach((element) => { element.hidden = true; });
  const placeholder = tableStep.querySelector('.table-picker-placeholder');
  if (placeholder) placeholder.textContent = 'Посмотреть схему и выбрать столик';
  opener.disabled = false;
}
