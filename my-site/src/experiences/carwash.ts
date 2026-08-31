import { createReservation, type ReservationPayload } from '../api';
import type { PublicTenantConfig } from '../tenancy/types';

// Car-wash booking experience. Initialized explicitly from main.ts when the
// resolved tenant's businessType is 'CarWash'. All element lookups are
// null-safe, so this no-ops when the shell markup is absent, mirroring the
// restaurant experience wiring.

interface CarWashElements {
  form: HTMLFormElement;
  plate: HTMLInputElement | null;
  phone: HTMLInputElement | null;
  scheduledAt: HTMLInputElement | null;
  service: HTMLSelectElement | HTMLInputElement | null;
  name: HTMLInputElement | null;
  remind: HTMLInputElement | null;
  submit: HTMLButtonElement | null;
  status: HTMLElement | null;
}

function resolveElements(): CarWashElements | null {
  const form = document.querySelector<HTMLFormElement>('[data-carwash="form"]');
  if (!form) return null;

  return {
    form,
    plate: form.querySelector<HTMLInputElement>('[data-carwash="plate"]'),
    phone: form.querySelector<HTMLInputElement>('[data-carwash="phone"]'),
    scheduledAt: form.querySelector<HTMLInputElement>('[data-carwash="scheduled-at"]'),
    service: form.querySelector<HTMLSelectElement | HTMLInputElement>('[data-carwash="service"]'),
    name: form.querySelector<HTMLInputElement>('[data-carwash="name"]'),
    remind: form.querySelector<HTMLInputElement>('[data-carwash="remind"]'),
    submit: form.querySelector<HTMLButtonElement>('[data-carwash="submit"]'),
    status: form.querySelector<HTMLElement>('[data-carwash="status"]'),
  };
}

function setStatus(el: HTMLElement | null, message: string, isError: boolean): void {
  if (!el) return;
  el.textContent = message;
  el.hidden = message.trim() === '';
  el.dataset.state = isError ? 'error' : 'ok';
}

function label(config: PublicTenantConfig, key: string, fallback: string): string {
  const value = config.businessUi?.[key];
  return value && value.trim() ? value.trim() : fallback;
}

/**
 * Wires the car-wash reservation form for the current tenant. Safe to call for
 * any tenant: if the car-wash markup is not present, it returns without side
 * effects.
 */
export function initCarWashExperience(config: PublicTenantConfig): void {
  const els = resolveElements();
  if (!els) return;

  const submittingLabel = label(config, 'submitting', 'Отправка…');
  const successLabel = label(config, 'success', 'Запись принята');

  els.form.addEventListener('submit', async (event) => {
    event.preventDefault();

    const plateNumber = els.plate?.value.trim() ?? '';
    const customerPhone = els.phone?.value.trim() ?? '';
    const scheduledAt = els.scheduledAt?.value.trim() ?? '';
    const washServiceType = els.service?.value.trim() ?? '';

    if (!plateNumber || !customerPhone || !scheduledAt || !washServiceType) {
      setStatus(els.status, label(config, 'validationError', 'Заполните все обязательные поля'), true);
      return;
    }

    const payload: ReservationPayload = {
      customerName: els.name?.value.trim() ?? '',
      customerPhone,
      scheduledAt,
      tablesId: '',
      section: '',
      remindBeforeHour: els.remind?.checked ?? false,
      plateNumber,
      washServiceType,
    };

    const previousLabel = els.submit?.textContent ?? '';
    if (els.submit) {
      els.submit.disabled = true;
      els.submit.textContent = submittingLabel;
    }
    setStatus(els.status, '', false);

    try {
      await createReservation(payload);
      setStatus(els.status, successLabel, false);
      els.form.reset();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Не удалось создать запись';
      setStatus(els.status, message, true);
    } finally {
      if (els.submit) {
        els.submit.disabled = false;
        els.submit.textContent = previousLabel || label(config, 'submit', 'Записаться');
      }
    }
  });
}
