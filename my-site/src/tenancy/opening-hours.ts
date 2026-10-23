import type { PublicOpeningHours, PublicTenantConfig } from './types';

interface OpeningHoursGroup {
  days: string;
  openTime: string;
  closeTime: string;
  closesNextDay: boolean;
}

/** Group adjacent weekdays with identical hours; an overnight close stays on its opening day. */
export function groupOpeningHours(hours: PublicOpeningHours[], locale = 'ru'): OpeningHoursGroup[] {
  const weekday = new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' });
  const label = (day: number): string => {
    const value = weekday.format(new Date(Date.UTC(2024, 0, day === 0 ? 7 : day)));
    return value.charAt(0).toLocaleUpperCase(locale) + value.slice(1);
  };
  const ordered = [...hours].sort((a, b) => (a.day + 6) % 7 - (b.day + 6) % 7);
  const groups: OpeningHoursGroup[] = [];
  let first: PublicOpeningHours | undefined;
  let previous: PublicOpeningHours | undefined;
  for (const entry of ordered) {
    if (first && previous && entry.day === (previous.day + 1) % 7
      && entry.openTime === previous.openTime && entry.closeTime === previous.closeTime
      && entry.closesNextDay === previous.closesNextDay) {
      groups[groups.length - 1].days = `${label(first.day)}–${label(entry.day)}`;
    } else {
      first = entry;
      groups.push({ days: label(entry.day), openTime: entry.openTime, closeTime: entry.closeTime, closesNextDay: entry.closesNextDay });
    }
    previous = entry;
  }
  return groups;
}

export function renderOpeningHours(config: PublicTenantConfig): void {
  const container = document.getElementById('reservationHours');
  if (!container || !config.openingHours?.length) return;
  container.classList.add('weekly-opening-hours');
  container.replaceChildren(document.createTextNode('Время работы:'));
  for (const group of groupOpeningHours(config.openingHours, config.locale)) {
    const row = document.createElement('span');
    row.className = 'opening-hours-row';
    const days = document.createElement('span');
    days.textContent = group.days;
    const times = document.createElement('span');
    for (const [index, value] of [group.openTime, group.closeTime].entries()) {
      if (index) times.append('–');
      const time = document.createElement('time');
      time.dateTime = value;
      time.textContent = value;
      times.append(time);
    }
    if (group.closesNextDay) times.setAttribute('aria-label', `${group.openTime}–${group.closeTime}, закрытие на следующий день`);
    row.append(days, times);
    container.append(row);
  }
}
