/// <reference lib="webworker" />
/**
 * Service worker of the web app (scope /app/), built by vite-plugin-pwa
 * (injectManifest) into wwwroot/app/sw.js.
 *
 * What it does, and nothing else:
 * - precaches the app shell: index.html, the hashed JS/CSS chunks, icons and
 *   fonts listed in the build manifest (`self.__WB_MANIFEST`);
 * - answers navigations inside /app/ network-first, falling back to the
 *   precached index.html when the network fails (offline shell);
 * - waits until the page asks it to take over (`SKIP_WAITING`), so a new
 *   version is only activated when the user clicks "Reload".
 *
 * It never handles /api/**, /hubs/**, /_blazor/**, /local/**, /img/**,
 * /webdav/**, media streams, Range requests or non-GET requests: no route
 * matches them, so the browser fetches them as if there were no service
 * worker. Nothing private (API data, thumbnails, files) is ever cached.
 */
import { clientsClaim } from 'workbox-core';
import { enable as enableNavigationPreload } from 'workbox-navigation-preload';
import { addRoute, cleanupOutdatedCaches, matchPrecache, precache } from 'workbox-precaching';
import { registerRoute } from 'workbox-routing';
import type { PrecacheEntry } from 'workbox-precaching';
import { isShellNavigation } from './routing';

declare const self: ServiceWorkerGlobalScope & { __WB_MANIFEST: (string | PrecacheEntry)[] };

const SHELL = 'index.html';
/** A navigation that takes longer than this falls back to the cached shell. */
const NAVIGATION_TIMEOUT_MS = 6000;

self.addEventListener('message', (event) => {
  if ((event.data as { type?: string } | null)?.type === 'SKIP_WAITING') void self.skipWaiting();
});

// First install: control the open tabs right away (updates still wait for SKIP_WAITING)
clientsClaim();
cleanupOutdatedCaches();
enableNavigationPreload();

precache(self.__WB_MANIFEST);

async function shell(): Promise<Response> {
  return (await matchPrecache(SHELL)) ?? Response.error();
}

// Navigations inside /app/: network first (index.html is no-cache on the
// server, so a deploy is seen at once), offline → the precached shell. A 5xx
// from a proxy while the server is down also gets the shell (the app then
// shows its own connection errors). Registered before the precache route so
// /app/ is never answered cache-first.
registerRoute(
  ({ request, url }) => isShellNavigation(request.mode, request.method, url, self.location.origin),
  async ({ request, event }) => {
    const network = (async () => ((await (event as FetchEvent).preloadResponse) as Response | undefined) ?? fetch(request))();
    network.catch(() => undefined); // handled below; avoids an unhandled rejection after a timeout
    let timer: ReturnType<typeof setTimeout> | undefined;
    const timeout = new Promise<null>((resolve) => (timer = setTimeout(() => resolve(null), NAVIGATION_TIMEOUT_MS)));
    try {
      const response = await Promise.race([network, timeout]);
      if (!response || (response.status >= 502 && response.status <= 504)) return await shell();
      return response;
    } catch {
      return shell();
    } finally {
      clearTimeout(timer);
    }
  }
);

// Precached files (hashed assets, icons...) cache-first. No directory index or
// clean URLs: only exact URLs of the build are answered.
addRoute({ directoryIndex: '', cleanURLs: false });
