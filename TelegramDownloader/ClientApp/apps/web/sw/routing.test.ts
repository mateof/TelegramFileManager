import { describe, expect, it } from 'vitest';
import { isShellNavigation } from './routing';

const origin = 'https://tfm.example';
const nav = (path: string, mode = 'navigate', method = 'GET', base = origin) => isShellNavigation(mode, method, new URL(path, base), origin);

describe('service worker routing', () => {
  it('answers navigations of the web app', () => {
    expect(nav('/app/')).toBe(true);
    expect(nav('/app/channels/42/music')).toBe(true);
    expect(nav('/app/search?q=x')).toBe(true);
  });

  it('never answers anything else', () => {
    expect(nav('/')).toBe(false); // classic Blazor UI
    expect(nav('/app')).toBe(false); // outside the /app/ scope
    expect(nav('/api-docs/index.html')).toBe(false);
    expect(nav('/api/v1/channels')).toBe(false);
    expect(nav('/hubs/events')).toBe(false);
    expect(nav('/_blazor')).toBe(false);
    expect(nav('/local/file.mp3')).toBe(false);
    expect(nav('/webdav/x')).toBe(false);
    expect(nav('/app/', 'cors')).toBe(false); // fetch() / XHR
    expect(nav('/app/', 'no-cors')).toBe(false); // <img>, <audio>
    expect(nav('/app/', 'navigate', 'POST')).toBe(false);
    expect(nav('/app/', 'navigate', 'GET', 'https://other.example')).toBe(false);
  });
});
