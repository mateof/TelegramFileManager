/**
 * Minimal i18n. The English text is the key: `t('Play All')`. Spanish is the
 * default language; a missing translation falls back to Spanish (regional
 * languages) or to the English key, so nothing ever renders blank.
 *
 * Placeholders use braces: `t('{count} files', { count: 3 })`.
 *
 * Dictionaries live in `./<lang>/*.ts` (one file per area, merged here), so
 * screens can be translated independently. Spanish is bundled with the app;
 * other languages are a lazy chunk that `main.tsx` loads before rendering
 * (`loadLanguage()`), so only the chosen language is downloaded. Hence no
 * `t()` may run while a module of the initial bundle is being evaluated.
 * (A top-level await here would do it for us, but it stops Rollup from
 * merging the initial chunks.)
 */
export type Language = 'es' | 'gl' | 'en';

export interface LanguageInfo {
  code: Language;
  /** The language's own name, never translated. */
  label: string;
  /** BCP 47 tag for dates and numbers. */
  locale: string;
}

export const LANGUAGES: LanguageInfo[] = [
  { code: 'es', label: 'Español', locale: 'es-ES' },
  { code: 'gl', label: 'Galego', locale: 'gl-ES' },
  { code: 'en', label: 'English', locale: 'en-GB' }
];

const LANGUAGE_KEY = 'tfm.web.language';

const esModules = import.meta.glob<{ default: Record<string, string> }>('./es/*.ts', { eager: true });
const es: Record<string, string> = Object.assign({}, ...Object.values(esModules).map((m) => m.default));

const lazyDictionaries: Partial<Record<Language, () => Promise<{ default: Record<string, string> }>>> = {
  gl: () => import('./gl')
};

const dictionaries: Partial<Record<Language, Record<string, string>>> = { es, en: {} };

function isLanguage(value: unknown): value is Language {
  return LANGUAGES.some((l) => l.code === value);
}

function readLanguage(): Language {
  try {
    const stored = localStorage.getItem(LANGUAGE_KEY);
    if (isLanguage(stored)) return stored;
  } catch {
    // ignore
  }
  // First visit: a Galician browser gets Galician, everyone else Spanish
  if (typeof navigator !== 'undefined' && navigator.languages?.some((l) => l.toLowerCase().startsWith('gl'))) return 'gl';
  return 'es';
}

async function loadDictionary(lang: Language): Promise<void> {
  const load = lazyDictionaries[lang];
  if (dictionaries[lang] || !load) return;
  try {
    dictionaries[lang] = (await load()).default;
  } catch {
    // Offline or a failed chunk: the Spanish texts are bundled
  }
}

let current: Language = readLanguage();

if (typeof document !== 'undefined') document.documentElement.lang = current;

/** Loads the dictionary of the current language (before the first render). */
export function loadLanguage(): Promise<void> {
  return loadDictionary(current);
}

export function getLanguage(): Language {
  return current;
}

/** The BCP 47 locale of a language (dates, numbers). */
export function getLocale(lang: Language = current): string {
  return LANGUAGES.find((l) => l.code === lang)?.locale ?? 'es-ES';
}

/**
 * Changing language reloads the app: every screen renders its strings from
 * scratch and nothing needs to subscribe to language changes.
 */
export function setLanguage(lang: Language): void {
  if (lang === current) return;
  try {
    localStorage.setItem(LANGUAGE_KEY, lang);
  } catch {
    // ignore
  }
  current = lang;
  window.location.reload();
}

export function t(key: string, vars?: Record<string, string | number>): string {
  const template = dictionaries[current]?.[key] ?? (current === 'en' ? undefined : es[key]) ?? key;
  if (!vars) return template;
  return template.replace(/\{(\w+)\}/g, (match, name: string) =>
    name in vars ? String(vars[name]) : match
  );
}

/** Test helper: switch language without reloading. */
export async function __setLanguageForTests(lang: Language): Promise<void> {
  await loadDictionary(lang);
  current = lang;
}

/** Keys used in the code but missing from a dictionary (dev aid). */
export function missingTranslations(keys: string[], lang: Language = 'es'): string[] {
  const dictionary = dictionaries[lang] ?? {};
  return keys.filter((k) => !(k in dictionary));
}
