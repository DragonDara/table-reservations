import { ApiError, createReservation, type ReservationPayload } from '../api';
import type { PublicTenantConfig } from '../tenancy/types';
import { carwashSlots, kazakhstanDate } from './carwash-schedule';

// Car-wash booking experience. Initialized explicitly from main.ts when the
// resolved tenant's businessType is 'CarWash'. All element lookups are
// null-safe, so this no-ops when the shell markup is absent, mirroring the
// restaurant experience wiring.

interface CarWashElements {
  form: HTMLFormElement;
  plate: HTMLInputElement | null;
  phone: HTMLInputElement | null;
  scheduledAt: HTMLInputElement | null;
  date: HTMLInputElement | null;
  times: HTMLElement | null;
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
    date: form.querySelector<HTMLInputElement>('[data-carwash="date"]'),
    times: form.querySelector<HTMLElement>('[data-carwash="times"]'),
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
  // ASP.NET dictionary keys retain their configured casing (e.g. Services).
  const value = Object.entries(config.businessUi ?? {})
    .find(([name]) => name.toLowerCase() === key.toLowerCase())?.[1];
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
  let submitting = false;
  const steps = [...els.form.querySelectorAll<HTMLFieldSetElement>('[data-carwash-step]')];
  const back = els.form.querySelector<HTMLButtonElement>('[data-carwash="back"]');
  const progress = els.form.querySelector<HTMLElement>('[data-carwash="progress"]');
  let currentStep = 0;

  function showStep(index: number, focus = true): void {
    currentStep = index;
    steps.forEach((step, stepIndex) => { step.hidden = stepIndex !== index; });
    if (progress) progress.textContent = `Шаг ${index + 1} из ${steps.length}`;
    if (back) back.hidden = index === 0;
    if (els?.submit) {
      els.submit.textContent = index === steps.length - 1 ? label(config, 'submit', 'Записаться') : 'Далее';
    }
    setStatus(els?.status ?? null, '', false);
    // Keep the current viewport and avoid opening the mobile keyboard on every step.
    if (focus) steps[index]?.querySelector<HTMLElement>('legend')?.focus({ preventScroll: true });
  }

  back?.addEventListener('click', () => {
    if (!submitting && currentStep > 0) showStep(currentStep - 1);
  });

  function validateStep(index: number): boolean {
    const step = steps[index];
    const input = step.querySelector<HTMLInputElement | HTMLSelectElement>('input:not([type="hidden"]), select');
    input?.setCustomValidity('');
    if (step.dataset.carwashStep === 'plate' && !els?.plate?.value.trim()) {
      input?.setCustomValidity('Укажите гос. номер автомобиля.');
    }
    if (step.dataset.carwashStep === 'phone') {
      const digits = els?.phone?.value.replace(/\D/g, '') ?? '';
      if (digits.length < 10 || digits.length > 15) {
        input?.setCustomValidity('Укажите полный номер телефона с кодом страны.');
      }
    }
    if (input && !input.checkValidity()) {
      showStep(index);
      input.reportValidity();
      return false;
    }
    if (step.dataset.carwashStep === 'time') {
      const selected = els?.scheduledAt?.value ?? '';
      if (!carwashSlots(els?.date?.value ?? '', config.bookingTime).includes(selected)) {
        renderTimes();
        showStep(index);
        setStatus(els?.status ?? null, selected ? 'Выбранное время уже прошло. Выберите время позже.' : 'Выберите время записи.', true);
        return false;
      }
    }
    return true;
  }

  els.form.addEventListener('input', (event) => {
    const target = event.target;
    if (target instanceof HTMLInputElement || target instanceof HTMLSelectElement) target.setCustomValidity('');
  });
  showStep(0, false);

  const nav = document.querySelector<HTMLElement>('.nav');
  const navToggle = document.getElementById('navtoggle');
  const closeNav = () => {
    nav?.classList.remove('nav-open');
    navToggle?.setAttribute('aria-expanded', 'false');
  };
  navToggle?.addEventListener('click', () => {
    const open = nav?.classList.toggle('nav-open') ?? false;
    navToggle.setAttribute('aria-expanded', String(open));
  });
  nav?.querySelectorAll('a').forEach((link) => link.addEventListener('click', closeNav));
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') closeNav();
  });

  const setText = (key: string, value: string) => {
    const element = document.querySelector<HTMLElement>(`[data-carwash="${key}"]`);
    if (element) element.textContent = value;
  };
  setText('plate-label', label(config, 'PlateLabel', 'Гос. номер'));
  setText('service-label', label(config, 'ServiceLabel', 'Тип мойки'));
  setText('start-time', config.bookingTime.startTime);
  setText('end-time', config.bookingTime.endTime);
  if (els.plate) els.plate.placeholder = label(config, 'PlatePlaceholder', 'A123BC');

  const services = [...new Set(label(config, 'Services', '').split('|').map((item) => item.trim()).filter(Boolean))];
  const serviceList = document.querySelector<HTMLElement>('[data-carwash="services"]');
  if (els.service instanceof HTMLSelectElement) {
    els.service.replaceChildren(new Option('Выберите услугу', ''));
    services.forEach((service) => {
      (els.service as HTMLSelectElement).add(new Option(service, service));
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'carwash-service-card';
      button.textContent = service;
      button.addEventListener('click', () => {
        if (submitting) return;
        if (els.service) els.service.value = service;
        showStep(1);
        document.getElementById('reservation')?.scrollIntoView({ behavior: 'smooth' });
      });
      serviceList?.appendChild(button);
    });
  }

  function renderTimes(): void {
    if (!els?.date || !els.times || !els.scheduledAt) return;
    els.scheduledAt.value = '';
    els.date.min = kazakhstanDate();
    els.times.replaceChildren();
    const slots = carwashSlots(els.date.value, config.bookingTime);
    setText('slot-status', slots.length ? '' : 'На эту дату времени для записи нет. Выберите другой день.');
    slots.forEach((slot) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'time-option';
      button.textContent = slot.slice(11) + (slot.slice(0, 10) !== els.date!.value ? ' (+1 день)' : '');
      button.setAttribute('aria-pressed', 'false');
      button.addEventListener('click', () => {
        els.times?.querySelectorAll('button').forEach((option) => {
          option.classList.toggle('active', option === button);
          option.setAttribute('aria-pressed', String(option === button));
        });
        els.scheduledAt!.value = slot;
      });
      els.times!.appendChild(button);
    });
  }

  if (els.date) {
    els.date.value = kazakhstanDate();
    els.date.addEventListener('change', renderTimes);
  }
  renderTimes();
  if (services.length === 0) {
    setStatus(els.status, 'Онлайн-запись пока недоступна: услуги не настроены.', true);
    if (els.submit) els.submit.disabled = true;
  }

  els.form.addEventListener('submit', async (event) => {
    event.preventDefault();
    if (submitting || services.length === 0) return;
    if (!validateStep(currentStep)) return;
    if (currentStep < steps.length - 1) {
      showStep(currentStep + 1);
      return;
    }
    // Recheck earlier answers before sending, including slots that expired while typing.
    for (let index = 0; index < steps.length; index++) {
      if (!validateStep(index)) return;
    }

    const plateNumber = els.plate?.value.trim().toUpperCase() ?? '';
    const customerPhone = els.phone?.value.trim() ?? '';
    const scheduledAt = els.scheduledAt?.value.trim() ?? '';
    const washServiceType = els.service?.value.trim() ?? '';

    if (!plateNumber || !customerPhone || !scheduledAt || !washServiceType) {
      setStatus(els.status, label(config, 'validationError', 'Заполните все обязательные поля'), true);
      return;
    }

    if (!carwashSlots(els.date?.value ?? '', config.bookingTime).includes(scheduledAt)) {
      renderTimes();
      showStep(steps.findIndex((step) => step.dataset.carwashStep === 'time'));
      setStatus(els.status, 'Выбранное время уже прошло. Выберите время позже.', true);
      return;
    }
    if (!services.includes(washServiceType)) return;
    const phoneDigits = customerPhone.replace(/\D/g, '');
    if (phoneDigits.length < 10 || phoneDigits.length > 15) {
      setStatus(els.status, 'Укажите полный номер телефона с кодом страны.', true);
      els.phone?.focus();
      return;
    }

    const payload: ReservationPayload = {
      customerName: els.name?.value.trim() ?? '',
      customerPhone,
      scheduledAt,
      tablesId: '',
      section: '',
      remindBeforeHour: config.features.showReminderOption && (els.remind?.checked ?? false),
      plateNumber,
      washServiceType,
    };

    submitting = true;
    els.form.setAttribute('aria-busy', 'true');
    steps.forEach((step) => { step.disabled = true; });
    if (back) back.disabled = true;
    if (els.submit) {
      els.submit.disabled = true;
      els.submit.textContent = submittingLabel;
    }
    setStatus(els.status, '', false);

    try {
      try {
        await createReservation(payload);
      } catch (error) {
        if (!(error instanceof ApiError) || error.code !== 'EXISTING_RESERVATION') throw error;
        const date = error.existing?.scheduledAt.replace('T', ' ') ?? '';
        if (!window.confirm(`У вас уже есть запись ${date}. Заменить её новой записью?`)) {
          setStatus(els.status, 'Предыдущая запись сохранена.', false);
          return;
        }
        await createReservation({ ...payload, overwrite: true });
      }
      els.form.reset();
      if (els.date) els.date.value = kazakhstanDate();
      renderTimes();
      showStep(0);
      setStatus(els.status, `${successLabel}. ${washServiceType} · ${plateNumber} · ${scheduledAt.replace('T', ' ')}`, false);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Не удалось создать запись';
      setStatus(els.status, message, true);
    } finally {
      submitting = false;
      els.form.setAttribute('aria-busy', 'false');
      steps.forEach((step) => { step.disabled = false; });
      if (back) back.disabled = false;
      if (els.submit) {
        els.submit.disabled = false;
        els.submit.textContent = currentStep === steps.length - 1 ? label(config, 'submit', 'Записаться') : 'Далее';
      }
    }
  });
}
