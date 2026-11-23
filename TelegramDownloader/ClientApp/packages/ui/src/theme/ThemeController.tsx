import { useEffect, useSyncExternalStore } from 'react';
import { accentOf, backgroundOf, LIGHT_ACCENT_MAP, resolveBackground, SHADES, type Palette, type Shade } from './palettes';
import { useAppearance } from './store';

const subscribers = new Map<string, (onChange: () => void) => () => void>();

function subscribeMedia(query: string) {
  let fn = subscribers.get(query);
  if (!fn) {
    fn = makeSubscriber(query);
    subscribers.set(query, fn);
  }
  return fn;
}

function makeSubscriber(query: string) {
  return (onChange: () => void) => {
    if (typeof window === 'undefined' || !window.matchMedia) return () => undefined;
    const mql = window.matchMedia(query);
    mql.addEventListener('change', onChange);
    return () => mql.removeEventListener('change', onChange);
  };
}

function readMedia(query: string): boolean {
  return typeof window !== 'undefined' && !!window.matchMedia?.(query).matches;
}

/** Live value of a CSS media query. */
export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(subscribeMedia(query), () => readMedia(query), () => false);
}

/** The background actually shown (`system` resolved) and whether it is light. */
export function useResolvedTheme() {
  const setting = useAppearance((s) => s.background);
  const prefersLight = useMediaQuery('(prefers-color-scheme: light)');
  const id = resolveBackground(setting, prefersLight);
  return { id, isLight: !!backgroundOf(id).isLight };
}

/** True when animations should be minimised (setting or OS preference). */
export function useReducedMotion(): boolean {
  const motion = useAppearance((s) => s.motion);
  const prefersReduced = useMediaQuery('(prefers-reduced-motion: reduce)');
  return motion === 'reduced' || (motion === 'system' && prefersReduced);
}

function writePalette(root: HTMLElement, name: string, palette: Palette, remap?: Partial<Record<Shade, Shade>>) {
  for (const shade of SHADES) {
    root.style.setProperty(`--color-${name}-${shade}`, palette[remap?.[shade] ?? shade]);
  }
}

function setMetaThemeColor(color: string) {
  let meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
  if (!meta) {
    meta = document.createElement('meta');
    meta.name = 'theme-color';
    document.head.appendChild(meta);
  }
  meta.content = color;
}

/**
 * Applies the appearance settings to <html>: colour scales, light/dark scheme,
 * browser theme colour and the effect switches (`data-glass`, `data-ambient`,
 * `data-motion`, `data-density`). Renders nothing; mount it once.
 */
export function ThemeController() {
  const accent = useAppearance((s) => s.accent);
  const glass = useAppearance((s) => s.glass);
  const ambient = useAppearance((s) => s.ambient);
  const density = useAppearance((s) => s.density);
  const { id: backgroundId, isLight } = useResolvedTheme();
  const reduced = useReducedMotion();

  useEffect(() => {
    const root = document.documentElement;
    const bg = backgroundOf(backgroundId);
    const acc = accentOf(accent);
    writePalette(root, 'neutral', bg.palette);
    writePalette(root, 'accent', acc.palette, isLight ? LIGHT_ACCENT_MAP : undefined);
    root.style.setProperty('--color-on-accent', acc.onAccent);
    root.style.colorScheme = isLight ? 'light' : 'dark';
    root.dataset.theme = bg.id;
    root.dataset.scheme = isLight ? 'light' : 'dark';
    setMetaThemeColor(bg.palette['950']);
  }, [accent, backgroundId, isLight]);

  useEffect(() => {
    const root = document.documentElement;
    root.dataset.glass = glass ? 'on' : 'off';
    root.dataset.ambient = ambient ? 'on' : 'off';
    root.dataset.motion = reduced ? 'reduced' : 'full';
    root.dataset.density = density;
  }, [glass, ambient, reduced, density]);

  return null;
}
