import type { HttpClient } from '../http';
import type {
  WebAuthChangePasswordRequest,
  WebAuthLoginRequest,
  WebAuthRemovePasswordRequest,
  WebAuthSettingsRequest,
  WebAuthSetupRequest,
  WebAuthStatusDto,
  WebPasskeyBeginDto,
  WebPasskeyDto,
  WebPasskeyListDto,
  WebPasskeyLoginFinishRequest,
  WebPasskeyRegisterFinishRequest,
  WebPasskeySettingsRequest
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
    removePassword: (body: WebAuthRemovePasswordRequest) => http.delete<void>(`${BASE}/password`, body, quiet),
    /**
     * Passkeys. Login calls work signed out; the rest need a browser session
     * (`403 session_required` otherwise) and `registerBegin` the current
     * password (`401 invalid_credentials`).
     */
    passkeys: {
      list: (signal?: AbortSignal) => http.get<WebPasskeyListDto>(`${BASE}/passkeys`, { signal }),
      registerBegin: (currentPassword: string) => http.post<WebPasskeyBeginDto>(`${BASE}/passkeys/register/begin`, { currentPassword }, quiet),
      registerFinish: (body: WebPasskeyRegisterFinishRequest) => http.post<WebPasskeyDto>(`${BASE}/passkeys/register/finish`, body, quiet),
      loginBegin: () => http.post<WebPasskeyBeginDto>(`${BASE}/passkeys/login/begin`, undefined, quiet),
      loginFinish: (body: WebPasskeyLoginFinishRequest) => http.post<WebAuthStatusDto>(`${BASE}/passkeys/login/finish`, body, quiet),
      rename: (id: string, name: string) => http.put<WebPasskeyDto>(`${BASE}/passkeys/${encodeURIComponent(id)}`, { name }),
      remove: (id: string) => http.delete<void>(`${BASE}/passkeys/${encodeURIComponent(id)}`),
      updateSettings: (body: WebPasskeySettingsRequest) => http.put<WebPasskeyListDto>(`${BASE}/passkeys/settings`, body)
    }
  };
}

export type WebAuthApi = ReturnType<typeof createWebAuthApi>;
