// Path helpers. Library paths are absolute, `/` is the root, no trailing slash.

/** `music//rock/` → `/music/rock`; `` → `/`. */
export function normalizePath(path: string | undefined | null): string {
  if (!path) return '/';
  const parts = path.replace(/\\/g, '/').split('/').filter((p) => p.length > 0 && p !== '.');
  return '/' + parts.join('/');
}

/** `/music` + `rock` → `/music/rock`. */
export function joinPath(base: string, name: string): string {
  const b = normalizePath(base);
  return normalizePath(b === '/' ? `/${name}` : `${b}/${name}`);
}

/** Parent folder; the root is its own parent. */
export function parentPath(path: string): string {
  const p = normalizePath(path);
  if (p === '/') return '/';
  const i = p.lastIndexOf('/');
  return i <= 0 ? '/' : p.slice(0, i);
}

/** Last segment (empty for the root). */
export function baseName(path: string): string {
  const p = normalizePath(path);
  return p === '/' ? '' : p.slice(p.lastIndexOf('/') + 1);
}

/** Segments with their cumulative paths: `/a/b` → `[{name:'a',path:'/a'},{name:'b',path:'/a/b'}]`. */
export function pathSegments(path: string): { name: string; path: string }[] {
  const p = normalizePath(path);
  if (p === '/') return [];
  const out: { name: string; path: string }[] = [];
  let acc = '';
  for (const name of p.slice(1).split('/')) {
    acc += `/${name}`;
    out.push({ name, path: acc });
  }
  return out;
}

/** True when `path` is `ancestor` or lives below it. */
export function isSameOrDescendant(path: string, ancestor: string): boolean {
  const p = normalizePath(path);
  const a = normalizePath(ancestor);
  if (a === '/') return true;
  return p === a || p.startsWith(a + '/');
}

/** Lower-case extension without dot (`Song.MP3` → `mp3`), undefined when none. */
export function extensionOf(name: string): string | undefined {
  const i = name.lastIndexOf('.');
  if (i <= 0 || i === name.length - 1) return undefined;
  return name.slice(i + 1).toLowerCase();
}

/** A valid entry name: not empty, no separators, not `.`/`..`. */
export function isValidName(name: string): boolean {
  const n = name.trim();
  return n.length > 0 && n !== '.' && n !== '..' && !/[\\/]/.test(n);
}
