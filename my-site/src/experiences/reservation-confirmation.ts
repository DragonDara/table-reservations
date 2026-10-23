import type { ReservationPayload } from '../api';

export interface ReservationConfirmation {
  venue: string;
  date: string;
  time: string;
  resource: string;
  duration: string;
}

/** API timestamps are venue wall-clock values: never interpret them in the guest's timezone. */
export function reservationConfirmation(
  booking: ReservationPayload, venue: string, durationMinutes: number, locale = 'ru',
): ReservationConfirmation {
  const wholeRoom = booking.section.includes('комната целиком');
  const resource = wholeRoom
    ? `VIP-комната №${booking.tablesId} целиком`
    : `Столик №${booking.tablesId}${booking.section ? ` · ${booking.section}` : ''}`;
  const minutes = Number.isInteger(durationMinutes) && durationMinutes > 0 ? durationMinutes : 60;
  const duration = new Intl.NumberFormat(locale, {
    style: 'unit', unit: minutes % 60 === 0 ? 'hour' : 'minute', unitDisplay: 'long',
  }).format(minutes % 60 === 0 ? minutes / 60 : minutes);
  const start = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(booking.scheduledAt)
    ? new Date(`${booking.scheduledAt}:00Z`) : new Date(NaN);
  if (!Number.isFinite(start.getTime()))
    return { venue, date: booking.scheduledAt, time: '', resource, duration };
  const end = new Date(start.getTime() + minutes * 60_000);
  const formatDate = new Intl.DateTimeFormat(locale, { dateStyle: 'long', timeZone: 'UTC' });
  const time = `${start.toISOString().slice(11, 16)}–${end.toISOString().slice(11, 16)}`;
  const nextDay = start.toISOString().slice(0, 10) !== end.toISOString().slice(0, 10);
  return {
    venue, date: formatDate.format(start),
    time: nextDay ? `${time} (${formatDate.format(end)})` : time,
    resource, duration,
  };
}

export function renderReservationConfirmation(container: HTMLElement, confirmation: ReservationConfirmation): void {
  container.replaceChildren();
  const rows = [
    ['Заведение', confirmation.venue], ['Дата', confirmation.date],
    ['Время (Атырау)', confirmation.time], ['Место', confirmation.resource], ['Длительность', confirmation.duration],
  ];
  for (const [label, value] of rows) {
    if (!value) continue;
    const row = document.createElement('div');
    const term = document.createElement('dt');
    term.textContent = label;
    const detail = document.createElement('dd');
    detail.textContent = value;
    row.append(term, detail);
    container.append(row);
  }
  container.hidden = false;
}
