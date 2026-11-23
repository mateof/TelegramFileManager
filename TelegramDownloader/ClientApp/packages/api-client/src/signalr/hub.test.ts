import { describe, expect, it, vi } from 'vitest';
import { TypedHub } from './hub';

describe('TypedHub', () => {
  it('sends the CSRF header on negotiate (the server rejects cookie POSTs under /hubs without it)', async () => {
    let resolveSeen!: (seen: { url: string; headers: Record<string, string> }) => void;
    const seen = new Promise<{ url: string; headers: Record<string, string> }>((r) => (resolveSeen = r));

    // SignalR picks fetch or XMLHttpRequest depending on the environment: watch both.
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input instanceof Request ? input.url : input);
      const headers: Record<string, string> = {};
      new Headers(init?.headers).forEach((v, k) => (headers[k] = v));
      if (url.includes('/negotiate')) resolveSeen({ url, headers });
      return new Response(null, { status: 403 });
    });
    let xhrUrl = '';
    const xhrHeaders: Record<string, string> = {};
    vi.spyOn(XMLHttpRequest.prototype, 'open').mockImplementation(function (_method: string, u: string | URL) {
      xhrUrl = String(u);
    });
    vi.spyOn(XMLHttpRequest.prototype, 'setRequestHeader').mockImplementation((name: string, value: string) => {
      xhrHeaders[name.toLowerCase()] = value;
    });
    vi.spyOn(XMLHttpRequest.prototype, 'send').mockImplementation(() => {
      if (xhrUrl.includes('/negotiate')) resolveSeen({ url: xhrUrl, headers: xhrHeaders });
    });

    const hub = new TypedHub('/hubs/test', { baseUrl: 'http://tfm.test', retryDelays: [60_000] });
    void hub.start();
    const { url, headers } = await seen;
    void hub.stop();

    expect(url).toContain('/hubs/test/negotiate');
    expect(headers['x-requested-with']).toBe('tfm');
  });
});
