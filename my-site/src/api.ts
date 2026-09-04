import { resolveOrganizationIdFallback } from './tenancy/tenant';
import type { PublicTenantConfig } from './tenancy/types';

export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '');

const ORGANIZATION_ID_HEADER = 'X-Organization-Id';

// Resolved once at module load. On production tenant subdomains this is null and
// the header is omitted, keeping the host authoritative for tenant resolution.
const organizationIdFallback = resolveOrganizationIdFallback();

function tenantHeaders(): Record<string, string> {
  return organizationIdFallback ? { [ORGANIZATION_ID_HEADER]: organizationIdFallback } : {};
}

export interface TableAvailability {
  id: number;
  type: 'Обычный' | 'VIP';
  seats: number;
  status: 'free' | 'occupied' | 'limited';
  nextReservationHours?: number;
}

export interface ReservationPayload {
  customerName: string;
  customerPhone: string;
  scheduledAt: string;
  tablesId: string;
  section: string;
  remindBeforeHour: boolean;
  // Car-wash tenants only; ignored by restaurant tenants.
  plateNumber?: string;
  washServiceType?: string;
  overwrite?: boolean;
}

export interface ExistingReservation {
  scheduledAt: string;
  tablesId: string;
  customerName: string;
}

export interface RatingResponse {
  rating: number;
  reviewCount: number;
}

export interface ReservationResponse {
  success?: boolean;
  reservationId?: string;
  id?: string;
  message?: string;
  status?: string;
  overwritten?: boolean;
  [key: string]: unknown;
}

export class ApiError extends Error {
  readonly status: number;
  code?: string;
  existing?: ExistingReservation;
  readonly body: unknown;

  constructor(message: string, status: number, body: unknown = null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;

    if (body && typeof body === 'object') {
      const record = body as Record<string, unknown>;
      this.code = typeof record.code === 'string' ? record.code : undefined;
      const existing = record.existing;
      if (existing && typeof existing === 'object') {
        const ex = existing as Record<string, unknown>;
        this.existing = {
          scheduledAt: String(ex.scheduledAt ?? ''),
          tablesId: String(ex.tablesId ?? ''),
          customerName: String(ex.customerName ?? ''),
        };
      }
    }
  }
}

function extractErrorMessage(payload: unknown, fallback: string): string {
  if (typeof payload === 'string' && payload.trim()) {
    return payload;
  }

  if (payload && typeof payload === 'object') {
    const record = payload as Record<string, unknown>;
    const message =
      (typeof record.message === 'string' && record.message)
      || (typeof record.title === 'string' && record.title)
      || (typeof record.detail === 'string' && record.detail);

    if (message) return message;
  }

  return fallback;
}
async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const url = `${API_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`;

  const response = await fetch(url, {
    method: 'GET',
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...tenantHeaders(),
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...(init.headers ?? {}),
    },
    ...init,
  });

  const contentType = response.headers.get('content-type') ?? '';
  let payload: unknown = null;

  if (contentType.includes('application/json')) {
    payload = await response.json().catch(() => null);
  } else {
    payload = await response.text().catch(() => null);
  }

  if (!response.ok) {
    throw new ApiError(
      extractErrorMessage(payload, 'Сервер вернул ошибку'),
      response.status,
      payload,
    );
  }

  if (payload && typeof payload === 'object') {
    const record = payload as Record<string, unknown>;

    if ('data' in record && record.data !== undefined) {
      return record.data as T;
    }

    if ('value' in record && record.value !== undefined) {
      return record.value as T;
    }
  }

  return payload as T;
}

export async function healthCheck(): Promise<{ status: string }> {
  return request<{ status: string }>('/health');
}

export async function getPublicTenantConfig(): Promise<PublicTenantConfig> {
  return request<PublicTenantConfig>('/tenant/public-config');
}

export async function getRating(): Promise<RatingResponse> {
  return request<RatingResponse>('/rating');
}

export async function getTables(scheduledAt?: string): Promise<TableAvailability[]> {
  const query = scheduledAt ? `?scheduledAt=${encodeURIComponent(scheduledAt)}` : '';
  return request<TableAvailability[]>(`/Tables${query}`);
}

export async function getAvailableSlots(date: string): Promise<string[]> {
  return request<string[]>(`/Tables/slots?date=${encodeURIComponent(date)}`);
}

export async function getTableAvailability(tablesId: string, scheduledAt?: string): Promise<TableAvailability> {
  const query = scheduledAt ? `?scheduledAt=${encodeURIComponent(scheduledAt)}` : '';
  return request<TableAvailability>(`/Tables/${encodeURIComponent(tablesId)}/availability${query}`);
}

export async function createReservation(payload: ReservationPayload): Promise<ReservationResponse> {
  return request<ReservationResponse>('/Reservations', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function getReservationStatus(reservationId: string): Promise<ReservationResponse> {
  return request<ReservationResponse>(`/Reservations/${encodeURIComponent(reservationId)}`);
}
