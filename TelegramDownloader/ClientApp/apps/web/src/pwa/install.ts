/**
 * "Install app" support. Kept tiny because it is imported by main.tsx: the
 * browser fires `beforeinstallprompt` once, early, so it must be captured
 * before any lazy chunk has loaded.
 */
import { create } from 'zustand';

/** Chrome / Edge / Samsung Internet event (not in the TS DOM lib). */
export interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  readonly userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
}

interface InstallState {
  /** The deferred prompt; null until the browser offers installation (and after it is used). */
  deferred: BeforeInstallPromptEvent | null;
  /** Running as an installed app (standalone window / home screen). */
  installed: boolean;
}

const STANDALONE_QUERIES = ['(display-mode: standalone)', '(display-mode: window-controls-overlay)', '(display-mode: fullscreen)'];

/** Whether the page runs as an installed app. */
export function isStandalone(win: Window = window): boolean {
  if ((win.navigator as Navigator & { standalone?: boolean }).standalone === true) return true; // iOS
  return STANDALONE_QUERIES.some((q) => win.matchMedia?.(q).matches);
}

/** iPhone / iPad (iPadOS reports itself as a Mac with touch). Every iOS browser installs through Share → Add to Home Screen. */
export function isIos(userAgent: string, maxTouchPoints: number): boolean {
  return /iPhone|iPad|iPod/i.test(userAgent) || (/Macintosh/i.test(userAgent) && maxTouchPoints > 1);
}

export const useInstallStore = create<InstallState>(() => ({
  deferred: null,
  installed: typeof window !== 'undefined' && isStandalone()
}));

let started = false;

/** Starts listening for the install prompt and for the app being installed. Call once at startup. */
export function startInstallCapture(win: Window = window): void {
  if (started) return;
  started = true;
  win.addEventListener('beforeinstallprompt', (event) => {
    // Keep the browser's mini-infobar away: the app offers "Install app" itself
    event.preventDefault();
    useInstallStore.setState({ deferred: event as BeforeInstallPromptEvent });
  });
  win.addEventListener('appinstalled', () => useInstallStore.setState({ deferred: null, installed: true }));
  for (const q of STANDALONE_QUERIES) {
    win.matchMedia?.(q).addEventListener?.('change', () => useInstallStore.setState({ installed: isStandalone(win) }));
  }
}

/** Shows the browser's install dialog. The prompt can be used only once. */
export async function promptInstall(): Promise<'accepted' | 'dismissed' | 'unavailable'> {
  const deferred = useInstallStore.getState().deferred;
  if (!deferred) return 'unavailable';
  useInstallStore.setState({ deferred: null });
  await deferred.prompt();
  const { outcome } = await deferred.userChoice;
  if (outcome === 'accepted') useInstallStore.setState({ installed: true });
  return outcome;
}

/**
 * What the UI can offer:
 * - `installed`: already running as an app, offer nothing;
 * - `prompt`: the browser offered installation, show "Install app";
 * - `insecure`: plain HTTP on a LAN address, installing needs HTTPS (or localhost);
 * - `ios`: explain Share → Add to Home Screen;
 * - `manual`: the browser did not offer it (unsupported, or it decides later); the browser menu may still have it.
 */
export type InstallMode = 'installed' | 'prompt' | 'insecure' | 'ios' | 'manual';

export function installModeOf(state: Pick<InstallState, 'deferred' | 'installed'>, env: { secure: boolean; ios: boolean }): InstallMode {
  if (state.installed) return 'installed';
  if (state.deferred) return 'prompt';
  if (!env.secure) return 'insecure';
  if (env.ios) return 'ios';
  return 'manual';
}

function environment() {
  if (typeof window === 'undefined') return { secure: true, ios: false };
  return { secure: window.isSecureContext, ios: isIos(navigator.userAgent, navigator.maxTouchPoints ?? 0) };
}

/** The current {@link InstallMode}, live. */
export function useInstallMode(): InstallMode {
  const deferred = useInstallStore((s) => s.deferred);
  const installed = useInstallStore((s) => s.installed);
  return installModeOf({ deferred, installed }, environment());
}
