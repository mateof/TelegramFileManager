import type { HttpClient } from '../http';
import type { AppConfigDto, UpdateConfigRequest } from '../types/config';

const BASE = '/api/v1/config';

/** Application configuration. */
export function createConfigApi(http: HttpClient) {
  return {
    /** GET /api/v1/config */
    get: (signal?: AbortSignal) => http.get<AppConfigDto>(BASE, { signal }),
    /** PATCH /api/v1/config: only the properties present are applied; answers the saved configuration. */
    update: (body: UpdateConfigRequest) => http.patch<AppConfigDto>(BASE, body)
  };
}

export type ConfigApi = ReturnType<typeof createConfigApi>;
