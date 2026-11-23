/** Keyboard model (pure): maps key events to file manager commands. */

export interface KeyLike {
  key: string;
  ctrlKey?: boolean;
  metaKey?: boolean;
  shiftKey?: boolean;
  altKey?: boolean;
}

export type FileManagerCommand =
  | { type: 'move'; delta: number; rows?: boolean; shift: boolean; ctrl: boolean }
  | { type: 'moveTo'; to: 'start' | 'end'; shift: boolean }
  | { type: 'page'; direction: 1 | -1; shift: boolean }
  | { type: 'open' }
  | { type: 'up' }
  | { type: 'back' }
  | { type: 'forward' }
  | { type: 'rename' }
  | { type: 'delete' }
  | { type: 'selectAll' }
  | { type: 'copy' }
  | { type: 'cut' }
  | { type: 'paste' }
  | { type: 'escape' }
  | { type: 'newFolder' }
  | { type: 'refresh' }
  | { type: 'toggleSelection' }
  | { type: 'search' }
  | { type: 'typeAhead'; char: string };

export function isMacPlatform(): boolean {
  if (typeof navigator === 'undefined') return false;
  const p = (navigator as Navigator & { userAgentData?: { platform?: string } }).userAgentData?.platform ?? navigator.platform ?? '';
  return /mac|iphone|ipad|ipod/i.test(p);
}

/**
 * Resolves the command of a key event in the item area. `columns` > 1 makes
 * Up/Down jump a whole row (grid view). Returns null for unhandled keys.
 */
export function resolveKey(e: KeyLike, options: { columns?: number; isMac?: boolean } = {}): FileManagerCommand | null {
  const mac = options.isMac ?? false;
  const mod = mac ? !!e.metaKey : !!e.ctrlKey;
  const shift = !!e.shiftKey;
  const columns = Math.max(1, options.columns ?? 1);
  const key = e.key;

  if (e.altKey) {
    if (key === 'ArrowLeft') return { type: 'back' };
    if (key === 'ArrowRight') return { type: 'forward' };
    if (key === 'ArrowUp') return { type: 'up' };
    return null;
  }
  if (mac && e.metaKey && key === 'ArrowUp') return { type: 'up' };
  if (mac && e.metaKey && key === 'ArrowDown') return { type: 'open' };
  if (mac && e.metaKey && key === 'Backspace') return { type: 'delete' };

  switch (key) {
    case 'ArrowDown':
      return { type: 'move', delta: columns, rows: columns > 1, shift, ctrl: mod };
    case 'ArrowUp':
      return { type: 'move', delta: -columns, rows: columns > 1, shift, ctrl: mod };
    case 'ArrowRight':
      return columns > 1 ? { type: 'move', delta: 1, shift, ctrl: mod } : null;
    case 'ArrowLeft':
      return columns > 1 ? { type: 'move', delta: -1, shift, ctrl: mod } : null;
    case 'Home':
      return { type: 'moveTo', to: 'start', shift };
    case 'End':
      return { type: 'moveTo', to: 'end', shift };
    case 'PageDown':
      return { type: 'page', direction: 1, shift };
    case 'PageUp':
      return { type: 'page', direction: -1, shift };
    case 'Enter':
      return { type: 'open' };
    case 'Backspace':
      return { type: 'up' };
    case 'F2':
      return { type: 'rename' };
    case 'F5':
      return { type: 'refresh' };
    case 'Delete':
      return { type: 'delete' };
    case 'Escape':
      return { type: 'escape' };
  }

  if (mod) {
    switch (key.toLowerCase()) {
      case 'a':
        return { type: 'selectAll' };
      case 'c':
        return { type: 'copy' };
      case 'x':
        return { type: 'cut' };
      case 'v':
        return { type: 'paste' };
      case 'f':
        return { type: 'search' };
      case 'n':
        return shift ? { type: 'newFolder' } : null;
      case ' ':
        return { type: 'toggleSelection' };
    }
    return null;
  }
  if (key === ' ') return { type: 'toggleSelection' };
  if (key.length === 1 && !e.ctrlKey && !e.metaKey) return { type: 'typeAhead', char: key };
  return null;
}

/** Does the event match a shortcut like `mod+shift+d`, `delete`, `alt+enter`? */
export function matchShortcut(shortcut: string, e: KeyLike, isMac = false): boolean {
  const parts = shortcut.toLowerCase().split('+').map((p) => p.trim());
  const key = parts[parts.length - 1];
  const want = new Set(parts.slice(0, -1));
  const mod = want.has('mod');
  const ctrl = want.has('ctrl') || (mod && !isMac);
  const meta = want.has('meta') || want.has('cmd') || (mod && isMac);
  if (!!e.ctrlKey !== ctrl || !!e.metaKey !== meta) return false;
  if (!!e.shiftKey !== want.has('shift') || !!e.altKey !== want.has('alt')) return false;
  const pressed = e.key.toLowerCase();
  return pressed === key || (key === 'space' && pressed === ' ') || (key === 'del' && pressed === 'delete');
}

/** Human label of a shortcut: `mod+shift+d` → `Ctrl+Shift+D` (or `⌘⇧D` on macOS). */
export function formatShortcut(shortcut: string, isMac = false): string {
  const names: Record<string, [string, string]> = {
    mod: ['Ctrl', '⌘'],
    ctrl: ['Ctrl', '⌃'],
    meta: ['Meta', '⌘'],
    cmd: ['Cmd', '⌘'],
    shift: ['Shift', '⇧'],
    alt: ['Alt', '⌥'],
    delete: ['Del', '⌫'],
    enter: ['Enter', '↩'],
    space: ['Space', 'Space']
  };
  const parts = shortcut
    .toLowerCase()
    .split('+')
    .map((p) => names[p.trim()]?.[isMac ? 1 : 0] ?? (p.length === 1 ? p.toUpperCase() : p.charAt(0).toUpperCase() + p.slice(1)));
  return parts.join(isMac ? '' : '+');
}

/**
 * Type-ahead: letters typed quickly form a prefix; the next entry whose name
 * starts with it gets the focus. Repeating the same letter cycles.
 */
export function createTypeAhead(timeoutMs = 700) {
  let buffer = '';
  let last = 0;
  return {
    /** Adds a char and returns the prefix to look for. */
    push(char: string, now = Date.now()): string {
      const c = char.toLocaleLowerCase();
      buffer = now - last > timeoutMs ? c : buffer + c;
      last = now;
      return buffer;
    },
    reset() {
      buffer = '';
      last = 0;
    }
  };
}

/** Index of the next name starting with `prefix`, starting after `fromIndex` (wraps). -1 when none. */
export function findTypeAhead(names: readonly string[], prefix: string, fromIndex: number): number {
  if (!prefix || names.length === 0) return -1;
  const p = prefix.toLocaleLowerCase();
  // Same letter repeated ("aaa") cycles through the entries starting with it
  const cycling = p.length > 1 && [...p].every((c) => c === p[0]);
  const needle = cycling ? p[0] : p;
  const start = cycling || p.length === 1 ? fromIndex + 1 : Math.max(0, fromIndex);
  for (let i = 0; i < names.length; i++) {
    const idx = (start + i) % names.length;
    if (names[idx].toLocaleLowerCase().startsWith(needle)) return idx;
  }
  return -1;
}
