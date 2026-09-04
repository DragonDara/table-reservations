import { getPublicTenantConfig } from '../api';
import type { PublicTenantConfig } from './types';
import { applyTenantTheme } from './theme';
import { applyTenantContent } from './content';

// Orchestrates tenant startup: load public config, apply branding/content, and
// toggle loading/unavailable states. Returns the resolved config so business
// experiences can initialize, or null when no tenant could be resolved (in which
// case a controlled unavailable state is shown instead of another org's brand).

const APP_ROOT_SELECTOR = '[data-app-root]';
const LOADING_SELECTOR = '[data-app-state="loading"]';
const UNAVAILABLE_SELECTOR = '[data-app-state="unavailable"]';

function toggle(selector: string, visible: boolean): void {
  const el = document.querySelector<HTMLElement>(selector);
  if (el) el.hidden = !visible;
}

function showLoading(): void {
  toggle(LOADING_SELECTOR, true);
  toggle(UNAVAILABLE_SELECTOR, false);
  toggle(APP_ROOT_SELECTOR, false);
}

function showUnavailable(): void {
  toggle(LOADING_SELECTOR, false);
  toggle(UNAVAILABLE_SELECTOR, true);
  toggle(APP_ROOT_SELECTOR, false);
}

function showApp(): void {
  toggle(LOADING_SELECTOR, false);
  toggle(UNAVAILABLE_SELECTOR, false);
  toggle(APP_ROOT_SELECTOR, true);
}

export async function bootstrapTenant(): Promise<PublicTenantConfig | null> {
  showLoading();

  let config: PublicTenantConfig;
  try {
    config = await getPublicTenantConfig();
  } catch (err) {
    console.error('Не удалось загрузить конфигурацию организации', err);
    showUnavailable();
    return null;
  }

  try {
    applyTenantTheme(config);
    applyTenantContent(config);
  } catch (err) {
    console.error('Ошибка применения оформления организации', err);
  }

  showApp();
  return config;
}
