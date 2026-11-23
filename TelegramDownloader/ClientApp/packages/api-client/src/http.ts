/**
 * Low level HTTP plumbing shared by every API module.
 *
 * - Every v1 endpoint answers the envelope `{ success, data, error, message, page }`;
 *   `request()` unwraps it and returns `data`, `requestPaged()` returns `{ items, page }`.
 * - A failed call (HTTP error or `success: false`) throws an {@link ApiError} carrying the
 *   machine readable `code` and the HTTP `status`.
 * - Requests are same-origin with the session cookie (`credentials: 'same-origin'`) and every
 *   non-GET request carries `X-Requested-With: tfm` (the server's CSRF guard).
 * - A 401 also fires the `onUnauthorized` callback and the `unauthorized` event of
 *   {@link apiEvents}, so the app can show its login screen from a single place.
 */

/** Pagination block of list responses (mirrors `PageInfo`). */
export interface PageInfo {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

/** Error detail of the envelope (mirrors `ApiError`). */
export interface ApiErrorBody {
  code: string;
  message: string;
  detail?: string | null;
}

/** The envelope every v1 endpoint returns (mirrors `ApiResult<T>`). */
export interface ApiEnvelope<T> {
  success: boolean;
  data: T | null;
  error?: ApiErrorBody | null;
  message?: string | null;
  page?: PageInfo | null;
}

/** A page of results: the unwrapped `data` plus the `page` block. */
export interface Paged<T> {
  items: T[];
  page: PageInfo;
}

/** Paging/sorting query shared by list endpoints (mirrors `PagedQuery`). */
export interface PagedQuery {
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

/** Canonical error codes (mirrors `ApiErrorCodes`, plus the web-auth and client side ones). */
export const ApiErrorCodes = {
  Unauthorized: 'unauthorized',
  NotLoggedIn: 'not_logged_in',
  SetupRequired: 'setup_required',
  InvalidRequest: 'invalid_request',
  NotFound: 'not_found',
  ChannelNotFound: 'channel_not_found',
  FileNotFound: 'file_not_found',
  TaskNotFound: 'task_not_found',
  PlaylistNotFound: 'playlist_not_found',
  Conflict: 'conflict',
  AlreadyRunning: 'already_running',
  Forbidden: 'forbidden',
  NotSupported: 'not_supported',
  ServiceUnavailable: 'service_unavailable',
  LibraryDisabled: 'library_disabled',
  InternalError: 'internal_error',
  InvalidCredentials: 'invalid_credentials',
  TooManyAttempts: 'too_many_attempts',
  /** Client side: the request never reached the server or the answer was not JSON. */
  NetworkError: 'network_error',
  /** Client side: the request was aborted through its AbortSignal. */
  Aborted: 'aborted'
} as const;

export type ApiErrorCode = (typeof ApiErrorCodes)[keyof typeof ApiErrorCodes] | (string & {});

/** Error thrown by every API call that does not succeed. */
export class ApiError extends Error {
  readonly code: ApiErrorCode;
  /** HTTP status (0 when the request failed before getting an answer). */
  readonly status: number;
  readonly detail?: string | null;

  constructor(code: ApiErrorCode, message: string, status: number, detail?: string | null) {
    super(message);
    this.name = 'ApiError';
    this.code = code;
    this.status = status;
    this.detail = detail;
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }

  get isAborted(): boolean {
    return this.code === ApiErrorCodes.Aborted;
  }
}

export function isApiError(value: unknown): value is ApiError {
  return value instanceof ApiError;
}

export type QueryValue = string | number | boolean | null | undefined | readonly (string | number | boolean)[];
export type Query = Record<string, QueryValue>;

export interface RequestOptions {
  query?: Query;
  /** JSON body (objects/arrays are serialised), or a FormData/Blob/string sent as-is. */
  body?: unknown;
  signal?: AbortSignal;
  headers?: Record<string, string>;
  /** Do not fire the global unauthorized event on 401 (used by the login calls themselves). */
  skipUnauthorizedEvent?: boolean;
}

export interface HttpClientOptions {
  /** Server origin, empty for same-origin (the default). No trailing slash. */
  baseUrl?: string;
  /** Custom fetch implementation (tests). */
  fetch?: typeof fetch;
  /** Called on every 401 answer. */
  onUnauthorized?: (error: ApiError) => void;
}

/** Global events of the API layer. `unauthorized` is dispatched on every 401. */
export const apiEvents = new EventTarget();
export const UNAUTHORIZED_EVENT = 'unauthorized';

const JSON_TYPE = 'application/json';

function isRawBody(body: unknown): body is BodyInit {
  return (
    typeof body === 'string' ||
    (typeof FormData !== 'undefined' && body instanceof FormData) ||
    (typeof Blob !== 'undefined' && body instanceof Blob) ||
    (typeof URLSearchParams !== 'undefined' && body instanceof URLSearchParams) ||
    body instanceof ArrayBuffer
  );
}

/** Builds `path?query`, skipping null/undefined values and repeating arrays (`ids=1&ids=2`). */
export function buildQuery(query?: Query): string {
  if (!query) return '';
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null) continue;
    if (Array.isArray(value)) {
      for (const v of value) params.append(key, String(v));
    } else {
      params.append(key, String(value));
    }
  }
  const s = params.toString();
  return s ? `?${s}` : '';
}

export class HttpClient {
  readonly baseUrl: string;
  private readonly fetchImpl: typeof fetch;
  private readonly onUnauthorized?: (error: ApiError) => void;

  constructor(options: HttpClientOptions = {}) {
    this.baseUrl = (options.baseUrl ?? '').replace(/\/+$/, '');
    // Bind lazily so tests can replace globalThis.fetch after the client exists
    this.fetchImpl = options.fetch ?? ((input, init) => globalThis.fetch(input, init));
    this.onUnauthorized = options.onUnauthorized;
  }

  /** Absolute (or same-origin relative) URL for a path, e.g. for `<img src>` or `<audio src>`. */
  url(path: string, query?: Query): string {
    return `${this.baseUrl}${path}${buildQuery(query)}`;
  }

  /** Sends a request and returns the raw Response (throws ApiError on network/abort/HTTP error). */
  async raw(method: string, path: string, options: RequestOptions = {}): Promise<Response> {
    const headers: Record<string, string> = { Accept: JSON_TYPE, ...options.headers };
    let body: BodyInit | undefined;
    if (options.body !== undefined) {
      if (isRawBody(options.body)) {
        body = options.body;
      } else {
        body = JSON.stringify(options.body);
        headers['Content-Type'] = JSON_TYPE;
      }
    }
    if (method.toUpperCase() !== 'GET' && method.toUpperCase() !== 'HEAD') {
      headers['X-Requested-With'] = 'tfm';
    }

    let response: Response;
    try {
      response = await this.fetchImpl(this.url(path, options.query), {
        method,
        headers,
        body,
        credentials: 'same-origin',
        signal: options.signal
      });
    } catch (err) {
      if (options.signal?.aborted || (err instanceof DOMException && err.name === 'AbortError')) {
        throw new ApiError(ApiErrorCodes.Aborted, 'The request was aborted', 0);
      }
      throw new ApiError(ApiErrorCodes.NetworkError, err instanceof Error ? err.message : 'Network error', 0);
    }

    if (!response.ok) {
      throw await this.errorFrom(response, options);
    }
    return response;
  }

  /** Sends a request and returns the unwrapped envelope (`data` + `page` + `message`). */
  async envelope<T>(method: string, path: string, options: RequestOptions = {}): Promise<ApiEnvelope<T>> {
    const response = await this.raw(method, path, options);
    const envelope = await this.parseEnvelope<T>(response);
    if (!envelope.success) {
      const e = envelope.error;
      throw new ApiError(e?.code ?? ApiErrorCodes.InternalError, e?.message || envelope.message || 'Request failed', response.status, e?.detail);
    }
    return envelope;
  }

  /** Sends a request and returns `data` (typed). */
  async request<T>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
    const envelope = await this.envelope<T>(method, path, options);
    return envelope.data as T;
  }

  /** Sends a request to a paged endpoint and returns `{ items, page }`. */
  async requestPaged<T>(method: string, path: string, options: RequestOptions = {}): Promise<Paged<T>> {
    const envelope = await this.envelope<T[]>(method, path, options);
    const items = envelope.data ?? [];
    const page = envelope.page ?? {
      page: 1,
      pageSize: items.length,
      totalItems: items.length,
      totalPages: items.length ? 1 : 0,
      hasNext: false,
      hasPrevious: false
    };
    return { items, page };
  }

  get<T>(path: string, options?: RequestOptions): Promise<T> {
    return this.request<T>('GET', path, options);
  }

  post<T = void>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.request<T>('POST', path, { ...options, body });
  }

  put<T = void>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.request<T>('PUT', path, { ...options, body });
  }

  patch<T = void>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.request<T>('PATCH', path, { ...options, body });
  }

  delete<T = void>(path: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.request<T>('DELETE', path, { ...options, body });
  }

  getPaged<T>(path: string, options?: RequestOptions): Promise<Paged<T>> {
    return this.requestPaged<T>('GET', path, options);
  }

  private async parseEnvelope<T>(response: Response): Promise<ApiEnvelope<T>> {
    if (response.status === 204) return { success: true, data: null };
    const text = await response.text();
    if (!text) return { success: true, data: null };
    try {
      return JSON.parse(text) as ApiEnvelope<T>;
    } catch {
      throw new ApiError(ApiErrorCodes.NetworkError, 'The server answer is not valid JSON', response.status, text.slice(0, 200));
    }
  }

  private async errorFrom(response: Response, options: RequestOptions): Promise<ApiError> {
    let code: string = defaultCodeFor(response.status);
    let message = response.statusText || `HTTP ${response.status}`;
    let detail: string | null | undefined;
    try {
      const text = await response.text();
      if (text) {
        const parsed = JSON.parse(text) as Partial<ApiEnvelope<unknown>> & { title?: string };
        if (parsed.error) {
          code = parsed.error.code || code;
          message = parsed.error.message || message;
          detail = parsed.error.detail;
        } else if (parsed.message) {
          message = parsed.message;
        } else if (parsed.title) {
          // ASP.NET ProblemDetails (model validation)
          message = parsed.title;
        }
      }
    } catch {
      // not JSON: keep the HTTP status text
    }
    const error = new ApiError(code, message, response.status, detail);
    if (response.status === 401 && !options.skipUnauthorizedEvent) {
      this.onUnauthorized?.(error);
      apiEvents.dispatchEvent(new CustomEvent(UNAUTHORIZED_EVENT, { detail: error }));
    }
    return error;
  }
}

function defaultCodeFor(status: number): string {
  switch (status) {
    case 400:
      return ApiErrorCodes.InvalidRequest;
    case 401:
      return ApiErrorCodes.Unauthorized;
    case 403:
      return ApiErrorCodes.Forbidden;
    case 404:
      return ApiErrorCodes.NotFound;
    case 409:
      return ApiErrorCodes.Conflict;
    case 429:
      return ApiErrorCodes.TooManyAttempts;
    case 503:
      return ApiErrorCodes.ServiceUnavailable;
    default:
      return ApiErrorCodes.InternalError;
  }
}
