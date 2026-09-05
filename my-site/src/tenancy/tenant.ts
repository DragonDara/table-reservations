// Tenant identity resolution for the frontend.
//
// Production tenant domains are resolved by the backend from the request host.
// A shared production host can instead use the explicit organization fallback.
//
// Local development / shared-host: there is no tenant subdomain, so we allow an
// explicit organization id via (in priority order):
//   1. `?org=` / `?organizationId=` query parameter (also persisted to storage)
//   2. `localStorage["organizationId"]`
//   3. Vite env `VITE_ORGANIZATION_ID`
// The resolved id is sent as the `X-Organization-Id` header by api.ts.

const STORAGE_KEY = 'organizationId';

function isLocalDevelopmentHost(): boolean {
  return import.meta.env.DEV
    && ['localhost', '127.0.0.1', '::1'].includes(window.location.hostname);
}

function reflectOrganizationInLocalUrl(organizationId: string): void {
  if (!isLocalDevelopmentHost()) return;

  const url = new URL(window.location.href);
  if (url.searchParams.has('org') || url.searchParams.has('organizationId')) return;

  url.searchParams.set('org', organizationId);
  window.history.replaceState(window.history.state, '', url);
}

function fromQuery(): string | null {
  try {
    const params = new URLSearchParams(window.location.search);
    const value = params.get('org') ?? params.get('organizationId');
    if (value && value.trim()) {
      const id = value.trim();
      try {
        window.localStorage.setItem(STORAGE_KEY, id);
      } catch {
        /* storage may be unavailable */
      }
      return id;
    }
  } catch {
    /* URL parsing unavailable */
  }
  return null;
}

function fromStorage(): string | null {
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);
    return value && value.trim() ? value.trim() : null;
  } catch {
    return null;
  }
}

function fromEnv(): string | null {
  const value = import.meta.env.VITE_ORGANIZATION_ID as string | undefined;
  return value && value.trim() ? value.trim() : null;
}

/**
 * Returns an explicit organization id for local/shared-host scenarios, or null
 * when the backend should resolve the tenant from the host.
 */
export function resolveOrganizationIdFallback(): string | null {
  const organizationId = fromQuery() ?? fromStorage() ?? fromEnv();
  if (organizationId) {
    reflectOrganizationInLocalUrl(organizationId);
  }

  return organizationId;
}
