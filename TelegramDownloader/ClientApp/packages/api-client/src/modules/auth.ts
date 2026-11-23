import type { HttpClient } from '../http';
import type { AuthStatusDto, LoginStepRequest, QrLoginDto, QrPasswordRequest, StartQrQuery, TelegramUserDto } from '../types/auth';

const BASE = '/api/v1/auth';

/**
 * Telegram session lifecycle (phone or QR login). The session is shared by every client of the
 * server: signing in or out here affects the web UI too.
 */
export function createAuthApi(http: HttpClient) {
  return {
    /** GET /api/v1/auth/status */
    status: (signal?: AbortSignal) => http.get<AuthStatusDto>(`${BASE}/status`, { signal }),
    /** GET /api/v1/auth/me (401 `not_logged_in` without a session) */
    me: (signal?: AbortSignal) => http.get<TelegramUserDto>(`${BASE}/me`, { signal }),
    /** POST /api/v1/auth/login: advances the phone login one step (phone, code, 2FA password). */
    login: (body: LoginStepRequest, signal?: AbortSignal) => http.post<AuthStatusDto>(`${BASE}/login`, body, { signal }),
    /** POST /api/v1/auth/qr: starts a QR login session. */
    startQr: (query: StartQrQuery = {}, signal?: AbortSignal) =>
      http.post<QrLoginDto>(`${BASE}/qr`, undefined, { query: { ...query }, signal }),
    /** GET /api/v1/auth/qr/{sessionId} */
    pollQr: (sessionId: string, signal?: AbortSignal) =>
      http.get<QrLoginDto>(`${BASE}/qr/${encodeURIComponent(sessionId)}`, { signal }),
    /** POST /api/v1/auth/qr/{sessionId}/password */
    qrPassword: (sessionId: string, body: QrPasswordRequest) =>
      http.post<QrLoginDto>(`${BASE}/qr/${encodeURIComponent(sessionId)}/password`, body),
    /** DELETE /api/v1/auth/qr/{sessionId} */
    cancelQr: (sessionId: string) => http.delete<void>(`${BASE}/qr/${encodeURIComponent(sessionId)}`),
    /** POST /api/v1/auth/logout: terminates the shared Telegram session. */
    logout: () => http.post<void>(`${BASE}/logout`)
  };
}

export type AuthApi = ReturnType<typeof createAuthApi>;
