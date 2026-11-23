/**
 * Palettes. Tailwind 4 resolves every colour utility through a CSS variable
 * (`bg-accent-500` → `var(--color-accent-500)`), so a theme simply rewrites
 * those variables on <html> at runtime (same technique as the TFM Player PWA):
 *
 * - the accent palette goes into the `accent` scale,
 * - the background palette goes into the `neutral` scale. The light theme is a
 *   REVERSED scale, so "dark surface / light text" classes such as
 *   `bg-neutral-900 text-neutral-100` turn into "light surface / dark text"
 *   without any component knowing which theme is active.
 *
 * Components should prefer the semantic tokens (`bg-surface`, `text-fg`,
 * `text-fg-muted`, `border-border`, `bg-primary`…) defined in styles.css.
 */

export const SHADES = ['50', '100', '200', '300', '400', '500', '600', '700', '800', '900', '950'] as const;
export type Shade = (typeof SHADES)[number];
export type Palette = Record<Shade, string>;

export type AccentId = 'indigo' | 'violet' | 'ocean' | 'teal' | 'emerald' | 'rose' | 'sunset' | 'amber';
export type BackgroundId = 'slate' | 'graphite' | 'midnight' | 'amoled' | 'light';
/** `system` follows the OS light/dark preference (light ↔ slate). */
export type BackgroundSetting = BackgroundId | 'system';

// Tailwind's own palettes (hex), so every accent looks as polished as the default ones
const indigo: Palette = { 50: '#eef2ff', 100: '#e0e7ff', 200: '#c7d2fe', 300: '#a5b4fc', 400: '#818cf8', 500: '#6366f1', 600: '#4f46e5', 700: '#4338ca', 800: '#3730a3', 900: '#312e81', 950: '#1e1b4b' };
const violet: Palette = { 50: '#f5f3ff', 100: '#ede9fe', 200: '#ddd6fe', 300: '#c4b5fd', 400: '#a78bfa', 500: '#8b5cf6', 600: '#7c3aed', 700: '#6d28d9', 800: '#5b21b6', 900: '#4c1d95', 950: '#2e1065' };
const sky: Palette = { 50: '#f0f9ff', 100: '#e0f2fe', 200: '#bae6fd', 300: '#7dd3fc', 400: '#38bdf8', 500: '#0ea5e9', 600: '#0284c7', 700: '#0369a1', 800: '#075985', 900: '#0c4a6e', 950: '#082f49' };
const teal: Palette = { 50: '#f0fdfa', 100: '#ccfbf1', 200: '#99f6e4', 300: '#5eead4', 400: '#2dd4bf', 500: '#14b8a6', 600: '#0d9488', 700: '#0f766e', 800: '#115e59', 900: '#134e4a', 950: '#042f2e' };
const emerald: Palette = { 50: '#ecfdf5', 100: '#d1fae5', 200: '#a7f3d0', 300: '#6ee7b7', 400: '#34d399', 500: '#10b981', 600: '#059669', 700: '#047857', 800: '#065f46', 900: '#064e3b', 950: '#022c22' };
const rose: Palette = { 50: '#fff1f2', 100: '#ffe4e6', 200: '#fecdd3', 300: '#fda4af', 400: '#fb7185', 500: '#f43f5e', 600: '#e11d48', 700: '#be123c', 800: '#9f1239', 900: '#881337', 950: '#4c0519' };
const orange: Palette = { 50: '#fff7ed', 100: '#ffedd5', 200: '#fed7aa', 300: '#fdba74', 400: '#fb923c', 500: '#f97316', 600: '#ea580c', 700: '#c2410c', 800: '#9a3412', 900: '#7c2d12', 950: '#431407' };
const amber: Palette = { 50: '#fffbeb', 100: '#fef3c7', 200: '#fde68a', 300: '#fcd34d', 400: '#fbbf24', 500: '#f59e0b', 600: '#d97706', 700: '#b45309', 800: '#92400e', 900: '#78350f', 950: '#451a03' };

export interface AccentDef {
  id: AccentId;
  label: string;
  palette: Palette;
  /** Text colour on top of the accent-500/600 fill. */
  onAccent: string;
}

export const ACCENTS: AccentDef[] = [
  { id: 'indigo', label: 'Indigo', palette: indigo, onAccent: '#ffffff' },
  { id: 'violet', label: 'Violet', palette: violet, onAccent: '#ffffff' },
  { id: 'ocean', label: 'Ocean', palette: sky, onAccent: '#ffffff' },
  { id: 'teal', label: 'Teal', palette: teal, onAccent: '#ffffff' },
  { id: 'emerald', label: 'Emerald', palette: emerald, onAccent: '#ffffff' },
  { id: 'rose', label: 'Rose', palette: rose, onAccent: '#ffffff' },
  { id: 'sunset', label: 'Sunset', palette: orange, onAccent: '#ffffff' },
  { id: 'amber', label: 'Amber', palette: amber, onAccent: '#1c1003' }
];

const slate: Palette = { 50: '#f8fafc', 100: '#f1f5f9', 200: '#e2e8f0', 300: '#cbd5e1', 400: '#94a3b8', 500: '#64748b', 600: '#475569', 700: '#334155', 800: '#1e293b', 900: '#111827', 950: '#0b1120' };
// Neutral graphite (zinc)
const graphite: Palette = { 50: '#fafafa', 100: '#f4f4f5', 200: '#e4e4e7', 300: '#d4d4d8', 400: '#a1a1aa', 500: '#71717a', 600: '#52525b', 700: '#3f3f46', 800: '#27272a', 900: '#18181b', 950: '#0e0e10' };
// Deep blue-black
const midnight: Palette = { 50: '#f5f7ff', 100: '#e8ecfb', 200: '#cdd5f3', 300: '#a7b3dd', 400: '#7f8bb8', 500: '#5b6591', 600: '#3e4670', 700: '#2a3157', 800: '#1a1f3d', 900: '#0f1229', 950: '#070818' };
// Pure black for OLED screens
const amoled: Palette = { 50: '#fafafa', 100: '#f4f4f5', 200: '#e4e4e7', 300: '#d4d4d8', 400: '#a1a1aa', 500: '#71717a', 600: '#3f3f46', 700: '#27272a', 800: '#161618', 900: '#0a0a0b', 950: '#000000' };
// Light: the slate scale reversed (see the comment at the top)
const light: Palette = { 50: '#0b1120', 100: '#111827', 200: '#1e293b', 300: '#334155', 400: '#475569', 500: '#64748b', 600: '#94a3b8', 700: '#cbd5e1', 800: '#e2e8f0', 900: '#f1f5f9', 950: '#f8fafc' };

export interface BackgroundDef {
  id: BackgroundId;
  label: string;
  palette: Palette;
  isLight?: boolean;
}

export const BACKGROUNDS: BackgroundDef[] = [
  { id: 'slate', label: 'Slate', palette: slate },
  { id: 'graphite', label: 'Graphite', palette: graphite },
  { id: 'midnight', label: 'Midnight', palette: midnight },
  { id: 'amoled', label: 'AMOLED black', palette: amoled },
  { id: 'light', label: 'Light', palette: light, isLight: true }
];

// In the light theme the light accent shades (used as text on dark) would be
// unreadable on white: use darker ones instead
export const LIGHT_ACCENT_MAP: Partial<Record<Shade, Shade>> = { 200: '700', 300: '700', 400: '600' };

export function accentOf(id: AccentId): AccentDef {
  return ACCENTS.find((a) => a.id === id) ?? ACCENTS[0];
}

export function backgroundOf(id: BackgroundId): BackgroundDef {
  return BACKGROUNDS.find((b) => b.id === id) ?? BACKGROUNDS[0];
}

/** Resolves `system` against the OS preference. */
export function resolveBackground(setting: BackgroundSetting, prefersLight: boolean): BackgroundId {
  if (setting !== 'system') return setting;
  return prefersLight ? 'light' : 'slate';
}
