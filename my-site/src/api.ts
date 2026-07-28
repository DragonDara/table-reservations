const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/$/, '');

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
  remindBeforeHour: number;
}



export interface ReservationResponse {
  success?: boolean;
  reservationId?: string;
  id?: string;
  message?: string;
  status?: string;
  [key: string]: unknown;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const url = `${API_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`;

  const response = await fetch(url, {
    method: 'GET',
    credentials: 'include',
    headers: {
      Accept: 'application/json',
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
    const message =
      (payload as Record<string, unknown> | null)?.message as string | undefined
      ?? (payload as Record<string, unknown> | null)?.title as string | undefined
      ?? (payload as Record<string, unknown> | null)?.detail as string | undefined
      ?? 'Сервер вернул ошибку';

    throw new Error(message);
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

export async function getTables(): Promise<TableAvailability[]> {
  return request<TableAvailability[]>('/Tables');
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