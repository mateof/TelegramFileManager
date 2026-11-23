import { createApiClient, isApiError } from '@tfm/api-client';

/**
 * The single API client of the app (same origin, session cookie). Import it
 * from features: `import { api } from '@/lib/api'`.
 *
 * Every 401 dispatches `apiEvents` → `unauthorized`; the AuthGate listens and
 * shows the login screen, so features never handle 401 themselves.
 */
export const api = createApiClient();

/** Message to show for any error thrown by the API layer. */
export function errorMessage(error: unknown): string {
  if (isApiError(error)) return error.message;
  if (error instanceof Error) return error.message;
  return String(error);
}
