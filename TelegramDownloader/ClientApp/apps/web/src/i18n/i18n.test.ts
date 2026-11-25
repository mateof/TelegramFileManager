import ts from 'typescript';
import { describe, expect, it } from 'vitest';
import { __setLanguageForTests, getLocale, LANGUAGES, missingTranslations, t } from './index';

// Every source file of the app, as text, to find the keys passed to t('...')
const sources = import.meta.glob<string>(['../**/*.ts', '../**/*.tsx', '!../**/*.test.ts', '!../**/*.test.tsx', '!./**'], {
  eager: true,
  query: '?raw',
  import: 'default'
});

const dictionaries = import.meta.glob<{ default: Record<string, string> }>('./es/*.ts', { eager: true });
const galician = import.meta.glob<{ default: Record<string, string> }>(['./gl/*.ts', '!./gl/index.ts'], { eager: true });

const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(',');

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
    for (const [file, mod] of [...Object.entries(dictionaries), ...Object.entries(galician)]) {
      const lang = file.slice(0, 5); // './es/' or './gl/': each language on its own
      for (const [key, value] of Object.entries(mod.default)) {
        const prev = seen.get(lang + key);
        if (prev && prev.value !== value) conflicts.push(`${key}: ${prev.file}="${prev.value}" vs ${file}="${value}"`);
        else if (!prev) seen.set(lang + key, { file, value });
      }
    }
    expect(conflicts).toEqual([]);
  });

  it('has a Galician translation for every key used in the code', async () => {
    await __setLanguageForTests('gl');
    const keys = usedKeys();
    const missing = missingTranslations([...keys.keys()], 'gl').map((k) => `${k}  (${keys.get(k)})`);
    expect(missing).toEqual([]);
    await __setLanguageForTests('en');
  });

  it('mirrors every Spanish area file in Galician, with the same keys and placeholders', () => {
    const problems: string[] = [];
    for (const [file, mod] of Object.entries(dictionaries)) {
      const gl = galician[file.replace('./es/', './gl/')]?.default;
      if (!gl) {
        problems.push(`${file}: no Galician file`);
        continue;
      }
      for (const [key, value] of Object.entries(mod.default)) {
        if (!(key in gl)) problems.push(`${file}: missing "${key}"`);
        else if (placeholders(gl[key]) !== placeholders(value)) problems.push(`${file}: placeholders of "${key}"`);
        else if (!gl[key].trim()) problems.push(`${file}: empty "${key}"`);
      }
      for (const key of Object.keys(gl)) if (!(key in mod.default)) problems.push(`${file}: extra "${key}"`);
    }
    expect(problems).toEqual([]);
  });

  it('interpolates placeholders and falls back to the key', async () => {
    await __setLanguageForTests('es');
    expect(t('{count} files', { count: 3 })).toBe('3 ficheros');
    expect(t('Untranslated {x}', { x: 1 })).toBe('Untranslated 1');
    await __setLanguageForTests('gl');
    expect(t('{count} files', { count: 3 })).toBe('3 ficheiros');
    expect(t('Untranslated {x}', { x: 1 })).toBe('Untranslated 1');
    await __setLanguageForTests('en');
    expect(t('{count} files', { count: 3 })).toBe('3 files');
  });

  it('never translates while a module is evaluated (Galician loads before the first render)', () => {
    const offenders: string[] = [];
    for (const [file, code] of Object.entries(sources)) {
      const sf = ts.createSourceFile(file, code, ts.ScriptTarget.Latest, true, file.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
      const visit = (node: ts.Node, inFunction: boolean) => {
        const nested = inFunction || ts.isFunctionLike(node);
        if (!nested && ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 't') {
          offenders.push(`${file}:${sf.getLineAndCharacterOfPosition(node.getStart()).line + 1}`);
        }
        ts.forEachChild(node, (child) => visit(child, nested));
      };
      visit(sf, false);
    }
    expect(offenders).toEqual([]);
  });

  it('gives each language its locale', () => {
    expect(getLocale('es')).toBe('es-ES');
    expect(getLocale('gl')).toBe('gl-ES');
    expect(getLocale('en')).toBe('en-GB');
    expect(LANGUAGES.map((l) => l.label)).toEqual(['Español', 'Galego', 'English']);
  });
});
