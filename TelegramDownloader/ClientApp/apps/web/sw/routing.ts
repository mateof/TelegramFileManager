/** Path prefix (= service worker scope) of the web app. */
export const APP_SCOPE = '/app/';

/**
 * Prefixes the service worker must never answer. They are outside the scope
 * (/app/) anyway; listing them keeps the rule explicit should the scope ever
 * widen.
 */
export const NEVER_HANDLED = ['/api/', '/hubs/', '/_blazor/', '/local/', '/img/', '/webdav/', '/swagger/', '/api-docs/'];

/** Whether a request is a page navigation of the web app (gets the offline shell). */
export function isShellNavigation(mode: string, method: string, url: URL, origin: string): boolean {
  if (mode !== 'navigate' || method !== 'GET') return false;
  if (url.origin !== origin) return false;
  const path = url.pathname;
  if (!path.startsWith(APP_SCOPE)) return false;
  return !NEVER_HANDLED.some((p) => path.startsWith(p));
}
