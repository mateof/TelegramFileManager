import { describe, expect, it } from 'vitest';
import { __setLanguageForTests, missingTranslations, t } from './index';

// Every source file of the app, as text, to find the keys passed to t('...')
const sources = import.meta.glob<string>(['../**/*.ts', '../**/*.tsx', '!../**/*.test.ts', '!../**/*.test.tsx', '!./**'], {
  eager: true,
  query: '?raw',
  import: 'default'
});

const dictionaries = import.meta.glob<{ default: Record<string, string> }>('./es/*.ts', { eager: true });

function usedKeys(): Map<string, string> {
  const keys = new Map<string, string>(); // key -> first file using it
  // t('literal') or t("literal"), with optional second argument
  const pattern = /\bt\(\s*(['"])((?:\\.|(?!\1).)*)\1/g;
  for (const [file, code] of Object.entries(sources)) {
    for (const match of code.matchAll(pattern)) {
      const key = match[2].replace(/\\(['"\\])/g, '$1');
      if (!keys.has(key)) keys.set(key, file);
    }
  }
  return keys;
}

describe('i18n', () => {
  it('has a Spanish translation for every key used in the code', () => {
    const keys = usedKeys();
    const missing = missingTranslations([...keys.keys()], 'es').map((k) => `${k}  (${keys.get(k)})`);
    expect(missing).toEqual([]);
  });

  it('does not translate the same key differently in two area files', () => {
    const seen = new Map<string, { file: string; value: string }>();
    const conflicts: string[] = [];
    for (const [file, mod] of Object.entries(dictionaries)) {
      for (const [key, value] of Object.entries(mod.default)) {
        const prev = seen.get(key);
        if (prev && prev.value !== value) conflicts.push(`${key}: ${prev.file}="${prev.value}" vs ${file}="${value}"`);
        else if (!prev) seen.set(key, { file, value });
      }
    }
    expect(conflicts).toEqual([]);
  });

  it('interpolates placeholders and falls back to the key', () => {
    __setLanguageForTests('es');
    expect(t('{count} files', { count: 3 })).toBe('3 ficheros');
    expect(t('Untranslated {x}', { x: 1 })).toBe('Untranslated 1');
    __setLanguageForTests('en');
    expect(t('{count} files', { count: 3 })).toBe('3 files');
  });
});
