// Public tenant configuration contracts. These mirror the backend
// `PublicTenantConfigResponse` returned by GET /api/tenant/public-config.
// They intentionally contain only browser-safe fields (no secrets).

export type BusinessType = 'Restaurant' | 'CarWash';

export interface PublicTheme {
  background?: string | null;
  surface?: string | null;
  text?: string | null;
  muted?: string | null;
  accent?: string | null;
  border?: string | null;
  warning?: string | null;
  fontFamily?: string | null;
  headingFontFamily?: string | null;
  borderRadius?: string | null;
}

export interface PublicAssets {
  logo?: string | null;
  favicon?: string | null;
  heroImage?: string | null;
  heroBackground?: string | null;
  gallery: string[];
}

export interface PublicContent {
  heroEyebrow?: string | null;
  heroTitle?: string | null;
  heroAccent?: string | null;
  heroDescription?: string | null;
  primaryCta?: string | null;
  secondaryCta?: string | null;
  footerCopyright?: string | null;
  footerTagline?: string | null;
}

export interface PublicLinks {
  menu?: string | null;
  map?: string | null;
  phone?: string | null;
  whatsApp?: string | null;
  instagram?: string | null;
  threads?: string | null;
}

export interface PublicFeatures {
  showRating: boolean;
  showHowItWorks: boolean;
  showMenuLink: boolean;
  showReminderOption: boolean;
  showSocialLinks: boolean;
}

export interface PublicTenantConfig {
  organizationId: string;
  businessType: BusinessType;
  locale: string;
  displayName: string;
  documentTitle: string;
  layoutVariant: string;
  theme: PublicTheme;
  assets: PublicAssets;
  content: PublicContent;
  links: PublicLinks;
  features: PublicFeatures;
  businessUi: Record<string, string>;
}
