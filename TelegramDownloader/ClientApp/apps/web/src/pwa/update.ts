import { toast } from '@tfm/ui';
import { t } from '@/i18n';

/** How often an open app asks the server for a new service worker. */
export const UPDATE_INTERVAL_MS = 60 * 60 * 1000;
/** Returning to the app checks again, but not more often than this. */
export const FOCUS_CHECK_GAP_MS = 60 * 1000;

const TOAST_ID = 'tfm-pwa-update';

/** The part of a ServiceWorkerRegistration the checks use. */
export interface UpdatableRegistration {
  readonly installing: ServiceWorker | null;
  update(): Promise<unknown>;
}

/**
 * Checks for a new version hourly and whenever the app comes back to the
 * foreground (focus / visible), at most once per {@link FOCUS_CHECK_GAP_MS}.
 * Offline, or while a new worker is already installing, it does nothing.
 * Returns the function that stops the checks.
 */
export function watchForUpdates(registration: UpdatableRegistration, win: Window = window, now: () => number = Date.now): () => void {
  let last = now();
  const check = () => {
    if (registration.installing || win.navigator.onLine === false) return;
    last = now();
    registration.update().catch(() => undefined); // offline / server down: next time
  };
  const onForeground = () => {
    if (win.document.visibilityState === 'visible' && now() - last >= FOCUS_CHECK_GAP_MS) check();
  };
  const timer = win.setInterval(check, UPDATE_INTERVAL_MS);
  win.addEventListener('focus', onForeground);
  win.document.addEventListener('visibilitychange', onForeground);
  return () => {
    win.clearInterval(timer);
    win.removeEventListener('focus', onForeground);
    win.document.removeEventListener('visibilitychange', onForeground);
  };
}

/**
 * "New version available → Reload": stays until used or closed. `reload`
 * activates the waiting service worker, which reloads the page.
 */
export function showUpdateToast(reload: () => unknown): void {
  toast(t('New version available'), {
    id: TOAST_ID,
    description: t('Reload the page to get the latest version.'),
    duration: Infinity,
    action: { label: t('Reload'), onClick: () => void reload() }
  });
}
