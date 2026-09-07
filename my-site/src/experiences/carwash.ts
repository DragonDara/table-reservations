import {
  ApiError,
  createReservation,
  getCarWashCatalog,
  getCarWashQuote,
  getCarWashAvailability,
  type CarWashCatalog,
  type CarWashQuote,
  type ReservationPayload,
} from "../api";
import type { PublicTenantConfig } from "../tenancy/types";
import { kazakhstanDate } from "./carwash-schedule";
import { createNavigationGuard, isChoiceStepAnswered } from "./carwash-navigation";

export function initCarWashExperience(config: PublicTenantConfig): void {
  const form = document.querySelector<HTMLFormElement>('[data-carwash="form"]');
  if (!form) return;
  const el = <T extends HTMLElement>(key: string) =>
    form.querySelector<T>(`[data-carwash="${key}"]`)!;
  const steps = [
    ...form.querySelectorAll<HTMLFieldSetElement>("[data-carwash-step]"),
  ];
  const submit = el<HTMLButtonElement>("submit");
  const back = el<HTMLButtonElement>("back");
  const date = el<HTMLInputElement>("date");
  const scheduledAt = el<HTMLInputElement>("scheduled-at");
  const categoryOptions = el<HTMLElement>("category-options");
  const serviceOptions = el<HTMLElement>("service-options");
  const times = el<HTMLElement>("times");
  const summary = el<HTMLElement>("summary");
  let catalog: CarWashCatalog | null = null;
  let categoryId = "";
  let selected = new Set<string>();
  let quote: CarWashQuote | null = null;
  let slots: string[] = [];
  let currentStep = 0;
  let busy = false;
  let revision = 0;
  const acceptNavigation = createNavigationGuard();
  const money = (value: number) => `${value.toLocaleString("ru-KZ")} ₸`;
  const selection = () => ({
    vehicleCategoryId: categoryId,
    serviceIds: [...selected],
  });
  function syncNavigation() {
    const step = steps[currentStep]?.dataset.carwashStep;
    const answered = isChoiceStepAnswered(step, categoryId, selected.size, slots.includes(scheduledAt.value));
    submit.disabled = busy || !catalog || !answered;
    back.disabled = busy;
  }
  function replaceOptions(container: HTMLElement, buttons: HTMLButtonElement[]) {
    const focused = document.activeElement;
    const key = focused instanceof HTMLButtonElement && container.contains(focused)
      ? focused.dataset.optionId : undefined;
    container.replaceChildren(...buttons);
    if (key) buttons.find(button => button.dataset.optionId === key && !button.disabled)
      ?.focus({ preventScroll: true });
  }
  function status(message = "", error = false) {
    const element = el<HTMLElement>("status");
    element.textContent = message;
    element.hidden = !message;
    element.dataset.state = error ? "error" : "ok";
  }
  function updateSummary() {
    summary.hidden = !selected.size;
    summary.textContent = quote
      ? `${quote.services.map((s) => s.name).join(" + ")} · ${money(quote.totalKzt)} · ${quote.durationMinutes} мин`
      : selected.size
        ? `Выбрано услуг: ${selected.size}. Стоимость и время уточним на следующем шаге.`
        : "";
  }
  function invalidate() {
    revision++;
    quote = null;
    slots = [];
    scheduledAt.value = "";
    times.replaceChildren();
    updateSummary();
    syncNavigation();
  }
  function showStep(index: number, focus = true) {
    currentStep = index;
    steps.forEach((step, i) => {
      step.hidden = i !== index;
    });
    el<HTMLElement>("progress").textContent =
      `Шаг ${index + 1} из ${steps.length}`;
    back.hidden = index === 0;
    submit.textContent = busy ? "Загрузка…" : index === steps.length - 1 ? "Записаться" : "Далее";
    status();
    syncNavigation();
    if (focus)
      steps[index]
        ?.querySelector<HTMLElement>("legend")
        ?.focus({ preventScroll: true });
  }
  function setBusy(value: boolean) {
    busy = value;
    form!.setAttribute("aria-busy", String(value));
    steps.forEach((step) => {
      step.disabled = value;
    });
    syncNavigation();
    submit.textContent = value
      ? "Загрузка…"
      : currentStep === steps.length - 1
        ? "Записаться"
        : "Далее";
  }
  function option(text: string, active: boolean, click: () => void) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "carwash-service-option";
    button.classList.toggle("active", active);
    button.setAttribute("aria-pressed", String(active));
    button.textContent = text;
    button.dataset.optionId = text;
    button.addEventListener("click", () => {
      if (!busy) click();
    });
    return button;
  }
  function renderCategories() {
    replaceOptions(categoryOptions,
      (catalog?.categories ?? []).map((category) =>
        option(category.name, categoryId === category.id, () => {
          if (categoryId !== category.id) {
            categoryId = category.id;
            selected.clear();
            invalidate();
            renderCategories();
            renderServices();
          }
          status();
        }),
      ),
    );
  }
  function renderServices() {
    const included = new Set(
      catalog?.packageItems
        .filter((item) => selected.has(item.packageServiceId))
        .map((item) => item.includedServiceId),
    );
    replaceOptions(serviceOptions,
      (catalog?.services ?? [])
        .filter((service) => service.vehicleCategoryId === categoryId)
        .map((service) => {
          const isIncluded = included.has(service.id);
          const button = option(
            `${service.name} · ${isIncluded ? "в пакете" : money(service.priceKzt)}`,
            selected.has(service.id) || isIncluded,
            () => {
              if (selected.has(service.id)) selected.delete(service.id);
              else {
                selected.add(service.id);
                catalog?.packageItems
                  .filter((item) => item.packageServiceId === service.id)
                  .forEach((item) => selected.delete(item.includedServiceId));
              }
              invalidate();
              renderServices();
              status();
            },
          );
          button.disabled = isIncluded;
          button.dataset.serviceId = service.id;
          button.dataset.optionId = service.id;
          return button;
        }),
    );
  }
  async function loadTimes() {
    const version = ++revision;
    slots = [];
    scheduledAt.value = "";
    times.replaceChildren();
    el<HTMLElement>("slot-status").textContent = "Проверяем свободные боксы…";
    try {
      const result = await getCarWashAvailability(date.value, selection());
      if (version !== revision) return;
      quote = result.quote;
      slots = result.slots;
      updateSummary();
      el<HTMLElement>("slot-status").textContent = slots.length
        ? "Время указано по Казахстану."
        : "Свободного времени нет. Нажмите «Назад» и выберите другую дату.";
      times.replaceChildren(
        ...slots.map((slot) => {
          const button = option(
            slot.slice(11) +
              (slot.slice(0, 10) !== date.value ? " (+1 день)" : ""),
            false,
            () => {
              scheduledAt.value = slot;
              times.querySelectorAll("button").forEach((item) => {
                item.classList.toggle("active", item === button);
                item.setAttribute("aria-pressed", String(item === button));
              });
              status();
              syncNavigation();
            },
          );
          button.classList.add("time-option");
          return button;
        }),
      );
    } catch (error) {
      if (version !== revision) return;
      el<HTMLElement>("slot-status").textContent =
        "Не удалось проверить время. Нажмите «Назад» и попробуйте снова.";
      throw error;
    }
  }
  function validate(index: number): boolean {
    const step = steps[index];
    let error = "";
    if (step.dataset.carwashStep === "category" && !categoryId)
      error = "Выберите категорию автомобиля.";
    if (step.dataset.carwashStep === "service" && !selected.size)
      error = "Выберите хотя бы одну услугу.";
    if (
      step.dataset.carwashStep === "time" &&
      (!slots.includes(scheduledAt.value) ||
        new Date(`${scheduledAt.value}:00+05:00`).getTime() <
          Date.now() + 5 * 60_000)
    )
      error = "Выберите доступное время записи.";
    const input = step.querySelector<HTMLInputElement>(
      'input:not([type="hidden"]):not([type="checkbox"])',
    );
    input?.setCustomValidity("");
    if (step.dataset.carwashStep === "plate" && !input?.value.trim())
      input?.setCustomValidity("Укажите гос. номер.");
    if (step.dataset.carwashStep === "phone") {
      const digits = input?.value.replace(/\D/g, "") ?? "";
      if (digits.length < 10 || digits.length > 15)
        input?.setCustomValidity(
          "Укажите полный номер телефона с кодом страны.",
        );
    }
    if (error || (input && !input.checkValidity())) {
      showStep(index);
      if (error) status(error, true);
      else input?.reportValidity();
      return false;
    }
    return true;
  }
  back.addEventListener("click", () => {
    if (!busy && currentStep > 0 && acceptNavigation()) showStep(currentStep - 1);
  });
  document.querySelectorAll<HTMLAnchorElement>('[data-carwash-action="services"]').forEach(link => {
    link.addEventListener("click", event => {
      if (busy) { event.preventDefault(); return; }
      showStep(steps.findIndex(step => step.dataset.carwashStep === (categoryId ? "service" : "category")), false);
    });
  });
  date.min = kazakhstanDate();
  date.value = date.min;
  const lastDay = new Date(`${date.min}T00:00:00Z`);
  lastDay.setUTCDate(lastDay.getUTCDate() + 6);
  date.max = lastDay.toISOString().slice(0, 10);
  date.addEventListener("change", invalidate);
  form.addEventListener("input", (event) => {
    if (event.target instanceof HTMLInputElement)
      event.target.setCustomValidity("");
  });
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (busy || !catalog || !validate(currentStep)) return;
    if (!acceptNavigation()) return;
    setBusy(true);
    try {
      if (currentStep < steps.length - 1) {
        if (steps[currentStep].dataset.carwashStep === "service") {
          quote = await getCarWashQuote(selection());
          selected = new Set(quote.services.map((service) => service.id));
          renderServices();
          updateSummary();
        }
        const next = currentStep + 1;
        showStep(next);
        if (steps[next].dataset.carwashStep === "time") await loadTimes();
        return;
      }
      for (let i = 0; i < steps.length; i++) if (!validate(i)) return;
      const payload: ReservationPayload = {
        ...selection(),
        plateNumber: el<HTMLInputElement>("plate").value.trim().toUpperCase(),
        customerName: el<HTMLInputElement>("name").value.trim(),
        customerPhone: el<HTMLInputElement>("phone").value.trim(),
        scheduledAt: scheduledAt.value,
        tablesId: "",
        section: "",
        remindBeforeHour: false,
      };
      let result;
      try {
        result = await createReservation(payload);
      } catch (error) {
        if (
          !(error instanceof ApiError) ||
          error.code !== "EXISTING_RESERVATION"
        )
          throw error;
        if (
          !window.confirm(
            `У вас уже есть запись ${error.existing?.scheduledAt.replace("T", " ") ?? ""}. Заменить её?`,
          )
        ) {
          status("Предыдущая запись сохранена.");
          return;
        }
        result = await createReservation({ ...payload, overwrite: true });
      }
      const confirmation = `Запись принята · ${payload.plateNumber} · ${payload.scheduledAt.replace("T", " ")} · ${money(Number(result.totalKzt))} · ${result.durationMinutes} мин`;
      form!.reset();
      categoryId = "";
      selected.clear();
      date.value = kazakhstanDate();
      invalidate();
      renderCategories();
      renderServices();
      showStep(0);
      status(confirmation);
    } catch (error) {
      if (error instanceof ApiError && error.code === "SLOT_TAKEN") {
        showStep(
          steps.findIndex((step) => step.dataset.carwashStep === "time"),
        );
        try {
          await loadTimes();
        } catch {
          /* Preserve booking conflict message. */
        }
      }
      status(
        error instanceof Error
          ? error.message
          : "Не удалось выполнить запрос. Попробуйте снова.",
        true,
      );
    } finally {
      setBusy(false);
    }
  });
  showStep(0, false);
  setBusy(true);
  void getCarWashCatalog()
    .then((result) => {
      catalog = result;
      if (!result.categories.length || !result.services.length)
        throw new Error("Онлайн-запись пока недоступна: услуги не настроены.");
      renderCategories();
      renderServices();
      const list = document.querySelector<HTMLElement>(
        '[data-carwash="services"]',
      );
      const unique = [
        ...new Map(
          result.services.map((service) => [service.id, service]),
        ).values(),
      ];
      list?.replaceChildren(
        ...unique.map((service) => {
          const card = document.createElement("a");
          card.className = "carwash-service-card";
          card.href = "#reservation";
          card.textContent = service.name;
          return card;
        }),
      );
    })
    .catch((error) => {
      catalog = null;
      status(
        error instanceof Error
          ? error.message
          : "Не удалось загрузить услуги. Обновите страницу.",
        true,
      );
    })
    .finally(() => setBusy(false));

  for (const [key, value] of [
    ["start-time", config.bookingTime.startTime],
    ["end-time", config.bookingTime.endTime],
  ]) {
    const element = document.querySelector<HTMLElement>(
      `[data-carwash="${key}"]`,
    );
    if (element) element.textContent = value;
  }
  const nav = document.querySelector<HTMLElement>(".nav");
  const toggle = document.getElementById("navtoggle");
  const close = () => {
    nav?.classList.remove("nav-open");
    toggle?.setAttribute("aria-expanded", "false");
    toggle?.setAttribute("aria-label", "Открыть меню");
  };
  toggle?.addEventListener("click", () => {
    const open = nav?.classList.toggle("nav-open") ?? false;
    toggle.setAttribute("aria-expanded", String(open));
    toggle.setAttribute("aria-label", open ? "Закрыть меню" : "Открыть меню");
  });
  nav
    ?.querySelectorAll("a")
    .forEach((link) => link.addEventListener("click", close));
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && nav?.classList.contains("nav-open")) {
      close();
      toggle?.focus({ preventScroll: true });
    }
  });
  document.addEventListener("click", (event) => {
    if (event.target instanceof Node && !nav?.contains(event.target) && !toggle?.contains(event.target))
      close();
  });
}
