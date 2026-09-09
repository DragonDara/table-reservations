import './style.css';
import './ui/error-notification.css';
import { initErrorNotifications, reportError } from './ui/error-notification';

initErrorNotifications();

async function start(): Promise<void> {
  const { bootstrapTenant } = await import('./tenancy/bootstrap');
  const config = await bootstrapTenant();
  if (!config) return;

  if (/^\/reservations\/?$/i.test(window.location.pathname)) {
    const { initReservationsPage } = await import('./experiences/reservations');
    await initReservationsPage(config);
    return;
  }

  if (config.businessType === 'CarWash') {
    const { initCarWashExperience } = await import('./experiences/carwash');
    initCarWashExperience(config);
  } else {
    const { initRestaurantExperience } = await import('./experiences/restaurant');
    await initRestaurantExperience(config);
  }

  // Social links can target the booking form without a URL fragment. Wait until
  // the tenant's form is mounted, then scroll once on the next rendered frame.
  if (new URLSearchParams(window.location.search).get('book') === '1') {
    window.requestAnimationFrame(() => {
      document.getElementById('reservation')?.scrollIntoView({
        behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth',
        block: 'start',
      });
    });
  }
}

void start().catch((error) => reportError(error, 'Не удалось открыть страницу. Попробуйте обновить её.'));
