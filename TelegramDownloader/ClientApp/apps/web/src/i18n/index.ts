/**
 * Minimal i18n. The English text is the key: `t('Play All')`. Spanish is the
 * default language; a missing translation falls back to the English key, so
 * nothing ever renders blank.
 *
 * Placeholders use braces: `t('{count} files', { count: 3 })`.
 *
 * Dictionaries live in `./es/*.ts` (one file per area, merged here), so
 * screens can be translated independently.
 */
export type Language = 'es' | 'en';

export const LANGUAGES: { code: Language; label: string }[] = [
  { code: 'es', label: 'Español' },
  { code: 'en', label: 'English' }
];

const LANGUAGE_KEY = 'tfm.web.language';

const esModules = import.meta.glob<{ default: Record<string, string> }>('./es/*.ts', { eager: true });
const es: Record<string, string> = Object.assign({}, ...Object.values(esModules).map((m) => m.default));

const dictionaries: Record<Language, Record<string, string>> = { es, en: {} };

function readLanguage(): Language {
  try {
    const stored = localStorage.getItem(LANGUAGE_KEY);
    if (stored === 'es' || stored === 'en') return stored;
  } catch {
    // ignore
  }
  return 'es';
}

let current: Language = readLanguage();

if (typeof document !== 'undefined') document.documentElement.lang = current;

export function getLanguage(): Language {
  return current;
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
  const template = dictionaries[current][key] ?? key;
  if (!vars) return template;
  return template.replace(/\{(\w+)\}/g, (match, name: string) =>
    name in vars ? String(vars[name]) : match
  );
}

/** Test helper: switch language without reloading. */
export function __setLanguageForTests(lang: Language): void {
  current = lang;
}

/** Keys used in the code but missing from a dictionary (dev aid). */
export function missingTranslations(keys: string[], lang: Language = 'es'): string[] {
  return keys.filter((k) => !(k in dictionaries[lang]));
}
