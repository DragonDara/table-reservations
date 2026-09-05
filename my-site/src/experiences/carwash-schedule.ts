import type { PublicBookingTime } from '../tenancy/types';

export function kazakhstanDate(now = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Almaty', year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(now);
}

/** Tenant slots use Kazakhstan wall-clock strings, as required by the API. */
export function carwashSlots(date: string, bookingTime: PublicBookingTime, now = new Date()): string[] {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || date < kazakhstanDate(now)) return [];
  const calendarDate = new Date(`${date}T00:00:00Z`);
  if (!Number.isFinite(calendarDate.getTime()) || calendarDate.toISOString().slice(0, 10) !== date) return [];
  return bookingTime.availableTimeSlots.filter((time) => /^([01]\d|2[0-3]):[0-5]\d$/.test(time)).map((time) => {
    const day = new Date(calendarDate);
    // Slots after midnight belong to the following calendar date of an overnight shift.
    if (time < bookingTime.startTime) day.setUTCDate(day.getUTCDate() + 1);
    return `${day.toISOString().slice(0, 10)}T${time}`;
  }).filter((slot) => new Date(`${slot}:00+05:00`).getTime() >= now.getTime() + 5 * 60_000);
}
