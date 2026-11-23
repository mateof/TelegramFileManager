import { describe, expect, it, vi } from 'vitest';
import { ApiError, apiEvents, buildQuery, HttpClient, UNAUTHORIZED_EVENT } from './http';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function clientWith(response: Response | (() => Promise<Response>)) {
  const fetchMock = vi.fn<typeof fetch>(async () => (typeof response === 'function' ? response() : response));
  return { http: new HttpClient({ fetch: fetchMock }), fetchMock };
}

describe('HttpClient', () => {
  it('unwraps the envelope data', async () => {
    const { http } = clientWith(jsonResponse({ success: true, data: { id: 1 }, error: null }));
    await expect(http.get<{ id: number }>('/api/v1/x')).resolves.toEqual({ id: 1 });
  });

  it('returns items and page for paged endpoints', async () => {
    const page = { page: 2, pageSize: 10, totalItems: 25, totalPages: 3, hasNext: true, hasPrevious: true };
    const { http } = clientWith(jsonResponse({ success: true, data: [1, 2], page }));
    await expect(http.getPaged<number>('/api/v1/x')).resolves.toEqual({ items: [1, 2], page });
  });

  it('throws ApiError with code and status from an error envelope', async () => {
    const { http } = clientWith(
      jsonResponse({ success: false, data: null, error: { code: 'channel_not_found', message: 'No such channel', detail: 'id=5' } }, 404)
    );
    const err = await http.get('/api/v1/channels/5').catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err).toMatchObject({ code: 'channel_not_found', status: 404, message: 'No such channel', detail: 'id=5' });
  });

  it('throws when a 200 answer says success: false', async () => {
    const { http } = clientWith(jsonResponse({ success: false, data: null, error: { code: 'conflict', message: 'Busy' } }));
    await expect(http.post('/api/v1/x')).rejects.toMatchObject({ code: 'conflict', status: 200 });
  });

  it('maps a non-JSON error to a code derived from the status', async () => {
    const { http } = clientWith(new Response('boom', { status: 503, statusText: 'Service Unavailable' }));
    await expect(http.get('/x')).rejects.toMatchObject({ code: 'service_unavailable', status: 503 });
  });

  it('reports network failures as network_error', async () => {
    const { http } = clientWith(() => Promise.reject(new TypeError('Failed to fetch')));
    await expect(http.get('/x')).rejects.toMatchObject({ code: 'network_error', status: 0 });
  });

  it('reports aborted requests as aborted', async () => {
    const controller = new AbortController();
    const fetchMock = vi.fn<typeof fetch>((_input, init) => {
      return new Promise<Response>((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
      });
    });
    const http = new HttpClient({ fetch: fetchMock });
    const pending = http.get('/x', { signal: controller.signal });
    controller.abort();
    const err = await pending.catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).isAborted).toBe(true);
  });

  it('sends the CSRF header and JSON body on non-GET requests, with same-origin credentials', async () => {
    const { http, fetchMock } = clientWith(jsonResponse({ success: true, data: null }));
    await http.post('/api/v1/x', { a: 1 });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/v1/x');
    const headers = init?.headers as Record<string, string>;
    expect(headers['X-Requested-With']).toBe('tfm');
    expect(headers['Content-Type']).toBe('application/json');
    expect(init?.body).toBe('{"a":1}');
    expect(init?.credentials).toBe('same-origin');
  });

  it('does not send the CSRF header on GET', async () => {
    const { http, fetchMock } = clientWith(jsonResponse({ success: true, data: null }));
    await http.get('/api/v1/x');
    const headers = fetchMock.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers['X-Requested-With']).toBeUndefined();
  });

  it('fires the unauthorized callback and event on 401', async () => {
    const onUnauthorized = vi.fn();
    const listener = vi.fn();
    apiEvents.addEventListener(UNAUTHORIZED_EVENT, listener);
    const http = new HttpClient({
      fetch: async () => jsonResponse({ success: false, error: { code: 'unauthorized', message: 'Login required' } }, 401),
      onUnauthorized
    });
    await expect(http.get('/api/v1/x')).rejects.toMatchObject({ status: 401, isUnauthorized: true });
    expect(onUnauthorized).toHaveBeenCalledOnce();
    expect(listener).toHaveBeenCalledOnce();
    apiEvents.removeEventListener(UNAUTHORIZED_EVENT, listener);
  });

  it('does not fire the unauthorized event when asked not to', async () => {
    const onUnauthorized = vi.fn();
    const http = new HttpClient({ fetch: async () => jsonResponse({ success: false }, 401), onUnauthorized });
    await expect(http.post('/login', {}, { skipUnauthorizedEvent: true })).rejects.toMatchObject({ code: 'unauthorized' });
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it('treats empty bodies as success with null data', async () => {
    const { http } = clientWith(new Response(null, { status: 204 }));
    await expect(http.delete('/x')).resolves.toBeNull();
  });

  it('prefixes the base URL', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => jsonResponse({ success: true, data: 1 }));
    const http = new HttpClient({ baseUrl: 'http://host:5000/', fetch: fetchMock });
    await http.get('/api/v1/x', { query: { q: 'a b' } });
    expect(fetchMock.mock.calls[0][0]).toBe('http://host:5000/api/v1/x?q=a+b');
  });
});

describe('buildQuery', () => {
  it('skips empty values and repeats arrays', () => {
    expect(buildQuery({ a: 1, b: undefined, c: null, d: ['x', 'y'], e: false })).toBe('?a=1&d=x&d=y&e=false');
    expect(buildQuery({})).toBe('');
    expect(buildQuery()).toBe('');
  });
});
