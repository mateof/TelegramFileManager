/**
 * Registers the service worker (/app/sw.js, scope /app/). Loaded with
 * `import()` after the page has loaded, so neither this module nor
 * workbox-window weigh on the initial bundle.
 */
import { registerSW } from 'virtual:pwa-register';
import { showUpdateToast, watchForUpdates } from './update';

let registered = false;

export function registerPwa(): void {
  if (registered || !('serviceWorker' in navigator)) return;
  registered = true;
  const updateSW = registerSW({
    immediate: true,
    // A new version is installed and waiting: let the user choose when to reload
    onNeedRefresh: () => showUpdateToast(() => updateSW(true)),
    onRegisteredSW: (_url, registration) => {
      if (registration) watchForUpdates(registration);
    },
    onRegisterError: (error: unknown) => console.warn('Service worker registration failed', error)
  });
}
