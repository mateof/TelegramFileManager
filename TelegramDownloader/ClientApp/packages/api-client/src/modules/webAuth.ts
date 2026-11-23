import type { HttpClient } from '../http';
import type {
  WebAuthChangePasswordRequest,
  WebAuthLoginRequest,
  WebAuthRemovePasswordRequest,
  WebAuthSettingsRequest,
  WebAuthSetupRequest,
  WebAuthStatusDto
} from '../types/webAuth';

const BASE = '/api/v1/web-auth';

/**
 * Web login of the browser UI. Errors: `invalid_credentials` (401) and
 * `too_many_attempts` (429) on login. These calls never fire the global
 * unauthorized event (they ARE the login).
 */
export function createWebAuthApi(http: HttpClient) {
  const quiet = { skipUnauthorizedEvent: true } as const;
  return {
    status: (signal?: AbortSignal) => http.get<WebAuthStatusDto>(`${BASE}/status`, { ...quiet, signal }),
    setup: (body: WebAuthSetupRequest) => http.post<void>(`${BASE}/setup`, body, quiet),
    login: (body: WebAuthLoginRequest) => http.post<void>(`${BASE}/login`, body, quiet),
    logout: () => http.post<void>(`${BASE}/logout`, undefined, quiet),
    changePassword: (body: WebAuthChangePasswordRequest) => http.post<void>(`${BASE}/password`, body, quiet),
    updateSettings: (body: WebAuthSettingsRequest) => http.put<void>(`${BASE}/settings`, body),
    removePassword: (body: WebAuthRemovePasswordRequest) => http.delete<void>(`${BASE}/password`, body, quiet)
  };
}

export type WebAuthApi = ReturnType<typeof createWebAuthApi>;
