import type { PublicTenantConfig } from './types';

// Applies tenant branding at runtime: CSS custom properties (theme tokens),
// document title, favicon, language, font links, and stable root attributes.
// Values are treated as data only; no tenant-provided HTML/CSS is injected.

const FONT_HOST_ALLOWLIST = ['fonts.googleapis.com', 'fonts.gstatic.com'];

function setVar(name: string, value?: string | null): void {
  if (value && value.trim()) {
    document.documentElement.style.setProperty(name, value.trim());
  }
}

function applyTheme(config: PublicTenantConfig): void {
  const t = config.theme ?? {};
  // Map semantic public tokens onto the existing CSS variable names used by style.css.
  setVar('--bg', t.background);
  setVar('--panel', t.surface);
  setVar('--text', t.text);
  setVar('--sectext', t.muted);
  setVar('--muted', t.muted);
  setVar('--accent', t.accent);
  setVar('--gold', t.accent);
  setVar('--whiteborder', t.border);
  setVar('--warning', t.warning);
  setVar('--radius', t.borderRadius);

  if (t.fontFamily && t.fontFamily.trim()) {
    document.documentElement.style.setProperty('--tenant-font', t.fontFamily.trim());
    document.body.style.fontFamily = t.fontFamily.trim();
  }
  setVar('--tenant-heading-font', t.headingFontFamily ?? t.fontFamily);
}

function isSafeAssetUrl(url: string): boolean {
  if (url.startsWith('/') || url.startsWith('./') || url.startsWith('../')) {
    return true;
  }
  try {
    const parsed = new URL(url, window.location.origin);
    return parsed.protocol === 'https:' || parsed.origin === window.location.origin;
  } catch {
    return false;
  }
}

function applyFavicon(config: PublicTenantConfig): void {
  const favicon = config.assets?.favicon;
  if (!favicon || !favicon.trim() || !isSafeAssetUrl(favicon)) {
    return;
  }

  let link = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
  if (!link) {
    link = document.createElement('link');
    link.rel = 'icon';
    document.head.appendChild(link);
  }
  link.href = favicon.trim();
}

function applyDocument(config: PublicTenantConfig): void {
  if (config.documentTitle && config.documentTitle.trim()) {
    document.title = config.documentTitle.trim();
  } else if (config.displayName && config.displayName.trim()) {
    document.title = config.displayName.trim();
  }

  if (config.locale && config.locale.trim()) {
    document.documentElement.lang = config.locale.trim();
  }
}

function applyRootAttributes(config: PublicTenantConfig): void {
  const root = document.documentElement;
  root.dataset.organization = config.organizationId;
  root.dataset.businessType = config.businessType;
  if (config.layoutVariant && config.layoutVariant.trim()) {
    root.dataset.layoutVariant = config.layoutVariant.trim();
  }
}

/** Loads only allow-listed Google Fonts stylesheets referenced by the tenant. */
export function loadTenantFonts(fontUrls: string[] | undefined): void {
  if (!fontUrls) return;
  for (const url of fontUrls) {
    try {
      const parsed = new URL(url, window.location.origin);
      if (!FONT_HOST_ALLOWLIST.includes(parsed.host)) {
        continue;
      }
      if (document.querySelector(`link[href="${parsed.href}"]`)) {
        continue;
      }
      const link = document.createElement('link');
      link.rel = 'stylesheet';
      link.href = parsed.href;
      document.head.appendChild(link);
    } catch {
      /* ignore invalid font url */
    }
  }
}

/** Applies all runtime tenant branding derived from the public config. */
export function applyTenantTheme(config: PublicTenantConfig): void {
  applyTheme(config);
  applyFavicon(config);
  applyDocument(config);
  applyRootAttributes(config);
}
