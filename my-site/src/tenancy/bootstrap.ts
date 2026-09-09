import { getPublicTenantConfig } from '../api';
import type { PublicTenantConfig } from './types';
import { applyTenantTheme } from './theme';
import { applyTenantContent } from './content';
import carwashPage from '../pages/carwash.html?raw';
import { reportError, showError } from '../ui/error-notification';

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
    reportError(err, 'Не удалось загрузить организацию. Нажмите «Повторить».');
    showUnavailable();
    return null;
  }

  try {
    if (config.businessType !== 'Restaurant' && config.businessType !== 'CarWash') {
      showError('Страница этой организации пока недоступна. Попробуйте позже.');
      showUnavailable();
      return null;
    }
    if (config.businessType === 'CarWash') {
      const root = document.querySelector<HTMLElement>(APP_ROOT_SELECTOR);
      if (root) root.innerHTML = carwashPage;
    }
    applyTenantTheme(config);
    applyTenantContent(config);
  } catch (err) {
    reportError(err, 'Не удалось отобразить страницу организации. Нажмите «Повторить».');
    showUnavailable();
    return null;
  }

  showApp();
  return config;
}
