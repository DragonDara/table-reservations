import type { PublicTenantConfig } from './types';

// Renders tenant text/links/visibility onto the shared shell. All tenant values
// are assigned as text nodes or element properties (never innerHTML), so tenant
// configuration cannot inject markup.

function setText(selector: string, value?: string | null): void {
  if (value == null) return;
  const el = document.querySelector<HTMLElement>(selector);
  if (el) el.textContent = value;
}

function setLink(selector: string, href?: string | null): void {
  const el = document.querySelector<HTMLAnchorElement>(selector);
  if (!el) return;
  if (href && href.trim()) {
    el.href = href.trim();
    el.hidden = false;
    el.closest('[data-hide-when-empty]')?.removeAttribute('hidden');
  } else {
    el.hidden = true;
  }
}

function setVisible(selector: string, visible: boolean): void {
  document.querySelectorAll<HTMLElement>(selector).forEach((el) => {
    el.hidden = !visible;
  });
}

function applyContent(config: PublicTenantConfig): void {
  const c = config.content ?? {};

  setText('[data-tenant="hero-eyebrow"]', c.heroEyebrow);
  setText('[data-tenant="hero-title"]', c.heroTitle ?? undefined);
  setText('[data-tenant="hero-accent"]', c.heroAccent);
  setText('[data-tenant="hero-description"]', c.heroDescription);
  setText('[data-tenant="primary-cta"]', c.primaryCta);
  setText('[data-tenant="secondary-cta"]', c.secondaryCta);
  setText('[data-tenant="footer-tagline"]', c.footerTagline);
  setText('[data-tenant="footer-copyright"]', c.footerCopyright);
  setText('[data-tenant="display-name"]', config.displayName);
}

function applyLinks(config: PublicTenantConfig): void {
  const l = config.links ?? {};

  setLink('[data-tenant-link="menu"]', l.menu);
  setLink('[data-tenant-link="map"]', l.map);
  setLink('[data-tenant-link="phone"]', l.phone ? `tel:${l.phone}` : null);
  setLink('[data-tenant-link="whatsapp"]', l.whatsApp);
  setLink('[data-tenant-link="instagram"]', l.instagram);
  setLink('[data-tenant-link="threads"]', l.threads);
}

function applyFeatures(config: PublicTenantConfig): void {
  const f = config.features;

  setVisible('[data-feature="rating"]', f.showRating);
  setVisible('[data-feature="how-it-works"]', f.showHowItWorks);
  setVisible('[data-feature="menu-link"]', f.showMenuLink);
  setVisible('[data-feature="reminder"]', f.showReminderOption);
  setVisible('[data-feature="social"]', f.showSocialLinks);

  // Business-experience sections. The shell keeps both, toggled by business type.
  const isRestaurant = config.businessType === 'Restaurant';
  setVisible('[data-experience="restaurant"]', isRestaurant);
  setVisible('[data-experience="carwash"]', !isRestaurant);
}

function applyHeroImage(config: PublicTenantConfig): void {
  const hero = document.querySelector<HTMLElement>('[data-tenant="hero-image"]');
  const image = config.assets?.heroImage;
  if (hero && image && image.trim()) {
    hero.style.backgroundImage = `url('${image.trim()}')`;
  }
}

/** Applies all shell content/link/feature rendering from the public config. */
export function applyTenantContent(config: PublicTenantConfig): void {
  applyContent(config);
  applyLinks(config);
  applyFeatures(config);
  applyHeroImage(config);
}
