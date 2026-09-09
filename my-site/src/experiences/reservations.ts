import { getReservations, type ReservationListItem } from '../api';
import type { PublicTenantConfig } from '../tenancy/types';
import { kazakhstanDate } from './carwash-schedule';
import './reservations.css';
import { reportError, showError } from '../ui/error-notification';

export async function initReservationsPage(config: PublicTenantConfig): Promise<void> {
  const root = document.querySelector<HTMLElement>('[data-app-root]');
  if (!root) return;

  document.title = `Бронирования — ${config.displayName}`;
  root.innerHTML = `
    <main class="reservations-page">
      <header class="reservations-header">
        <div><p class="reservations-brand"></p><h1>Бронирования</h1></div>
        <a class="reservations-button" data-booking-link>Новая бронь</a>
      </header>
      <p class="reservations-description">Занятость на выбранную дату, включая брони с переходом через полночь. Время Казахстана (UTC+5).</p>
      <form class="reservations-toolbar">
        <label for="reservations-date">Дата<input id="reservations-date" type="date" min="0001-01-02" max="9999-12-30" required></label>
        <button class="reservations-button" type="submit">Обновить</button>
      </form>
      <p class="reservations-status" role="status" aria-live="polite"></p>
      <ol class="reservations-list" aria-label="Список бронирований"></ol>
    </main>`;

  root.querySelector('.reservations-brand')!.textContent = config.displayName;
  const bookingLink = root.querySelector<HTMLAnchorElement>('[data-booking-link]')!;
  bookingLink.href = `/?org=${encodeURIComponent(config.organizationId)}#reservation`;
  const dateInput = root.querySelector<HTMLInputElement>('#reservations-date')!;
  const form = root.querySelector<HTMLFormElement>('form')!;
  const refresh = form.querySelector<HTMLButtonElement>('button')!;
  const status = root.querySelector<HTMLElement>('.reservations-status')!;
  const list = root.querySelector<HTMLOListElement>('.reservations-list')!;
  dateInput.value = new URLSearchParams(window.location.search).get('date') ?? kazakhstanDate();
  if (!dateInput.value || !dateInput.validity.valid) dateInput.value = kazakhstanDate();
  let requestId = 0;

  function render(items: ReservationListItem[]): void {
    const rows = items.map((item) => {
      const row = document.createElement('li');
      row.className = 'reservations-row';
      const time = document.createElement('time');
      time.dateTime = `${item.scheduledAt}+05:00`;
      time.textContent = item.scheduledAt.slice(11, 16);
      const timeRange = document.createElement('div');
      timeRange.className = 'reservations-time';
      const endTime = document.createElement('time');
      endTime.dateTime = `${item.endsAt}+05:00`;
      endTime.textContent = item.endsAt.slice(11, 16);
      timeRange.append(time, document.createTextNode(' — '), endTime);
      const details = document.createElement('div');
      const title = document.createElement('h2');
      title.textContent = config.businessType === 'CarWash'
        ? item.washServiceType || 'Мойка автомобиля'
        : item.tablesId ? `Стол № ${item.tablesId}` : 'Стол не указан';
      const customer = document.createElement('p');
      customer.textContent = `Клиент: ${item.customerName?.trim() || 'Не указан'}`;
      const phone = document.createElement('p');
      phone.textContent = `Телефон: ${item.customerPhone?.trim() || 'Не указан'}`;
      const date = document.createElement('p');
      const startDate = item.scheduledAt.slice(0, 10);
      const endDate = item.endsAt.slice(0, 10);
      const formatDate = (value: string) => value.split('-').reverse().join('.');
      date.textContent = formatDate(startDate)
        + (startDate !== endDate ? ` — ${formatDate(endDate)}` : '')
        + (item.boxId ? ` · Бокс: ${item.boxId}` : '');
      details.append(title, customer, phone, date);
      row.append(timeRange, details);
      return row;
    });
    list.replaceChildren(...rows);
  }

  async function load(): Promise<void> {
    const currentRequest = ++requestId;
    list.replaceChildren();
    if (!dateInput.validity.valid || !dateInput.value) {
      refresh.disabled = false;
      list.setAttribute('aria-busy', 'false');
      status.textContent = '';
      showError('Выберите корректную дату.');
      return;
    }
    refresh.disabled = true;
    const url = new URL(window.location.href);
    url.searchParams.set('date', dateInput.value);
    window.history.replaceState(window.history.state, '', url);
    list.setAttribute('aria-busy', 'true');
    status.textContent = 'Загружаем бронирования…';
    try {
      const items = await getReservations(dateInput.value);
      if (currentRequest !== requestId) return;
      render(items);
      status.textContent = items.length
        ? `Бронирований: ${items.length}`
        : 'На эту дату бронирований пока нет.';
    } catch (error) {
      if (currentRequest !== requestId) return;
      status.textContent = '';
      reportError(error, 'Не удалось загрузить бронирования. Нажмите «Обновить», чтобы попробовать снова.');
    } finally {
      if (currentRequest === requestId) {
        refresh.disabled = false;
        list.setAttribute('aria-busy', 'false');
      }
    }
  }

  form.addEventListener('submit', (event) => { event.preventDefault(); void load(); });
  dateInput.addEventListener('change', () => void load());
  await load();
}
