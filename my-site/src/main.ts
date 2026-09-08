import './style.css';
import { bootstrapTenant } from './tenancy/bootstrap';

async function start(): Promise<void> {
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
}

void start().catch((error) => console.error('Ошибка инициализации приложения', error));
