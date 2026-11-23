import { describe, expect, it } from 'vitest';
import type { FileEntry } from '../types';
import { groupActions, resolveActions, selectionMatches } from './actions';
import { pasteBlockReason, type ClipboardState } from './clipboard';
import { createTypeAhead, findTypeAhead, formatShortcut, matchShortcut, resolveKey } from './keyboard';
import { mediaKindOf } from './media';
import { canGoBack, canGoForward, createHistory, currentPath, goBack, goForward, goUp, pushPath, syncPath } from './navigation';
import { baseName, extensionOf, isSameOrDescendant, isValidName, joinPath, normalizePath, parentPath, pathSegments } from './paths';
import { emptySelection, selectionReducer, type SelectionState } from './selection';
import { filterEntries, sortEntries } from './sortFilter';

const entry = (path: string, kind: 'file' | 'folder' = 'file', extra: Partial<FileEntry> = {}): FileEntry => ({
  id: path,
  name: baseName(path),
  kind,
  path,
  parentPath: parentPath(path),
  extension: kind === 'file' ? extensionOf(path) : undefined,
  ...extra
});

describe('paths', () => {
  it('normalizes and joins', () => {
    expect(normalizePath('')).toBe('/');
    expect(normalizePath('music//rock/')).toBe('/music/rock');
    expect(joinPath('/', 'a')).toBe('/a');
    expect(joinPath('/a', 'b')).toBe('/a/b');
    expect(parentPath('/a/b')).toBe('/a');
    expect(parentPath('/a')).toBe('/');
    expect(parentPath('/')).toBe('/');
    expect(pathSegments('/a/b')).toEqual([
      { name: 'a', path: '/a' },
      { name: 'b', path: '/a/b' }
    ]);
  });

  it('detects descendants, extensions and valid names', () => {
    expect(isSameOrDescendant('/a/b', '/a')).toBe(true);
    expect(isSameOrDescendant('/ab', '/a')).toBe(false);
    expect(extensionOf('Song.MP3')).toBe('mp3');
    expect(extensionOf('.bashrc')).toBeUndefined();
    expect(isValidName('ok.txt')).toBe(true);
    expect(isValidName('a/b')).toBe(false);
    expect(isValidName('  ')).toBe(false);
    expect(mediaKindOf('x.flac')).toBe('audio');
    expect(mediaKindOf('.mkv')).toBe('video');
    expect(mediaKindOf('pdf')).toBe('document');
  });
});

describe('selection', () => {
  const order = ['a', 'b', 'c', 'd', 'e'];
  const run = (actions: Parameters<typeof selectionReducer>[1][], start: SelectionState = emptySelection) =>
    actions.reduce((s, a) => selectionReducer(s, a, order), start);
  const ids = (s: SelectionState) => [...s.selected].sort();

  it('click selects one, ctrl toggles, shift ranges from the anchor', () => {
    let s = run([{ type: 'click', id: 'b' }]);
    expect(ids(s)).toEqual(['b']);
    s = run([{ type: 'click', id: 'd', ctrl: true }], s);
    expect(ids(s)).toEqual(['b', 'd']);
    s = run([{ type: 'click', id: 'b', ctrl: true }], s);
    expect(ids(s)).toEqual(['d']);
    s = run([{ type: 'click', id: 'b' }, { type: 'click', id: 'e', shift: true }]);
    expect(ids(s)).toEqual(['b', 'c', 'd', 'e']);
    // Shift range again from the same anchor shrinks it
    s = run([{ type: 'click', id: 'c', shift: true }], s);
    expect(ids(s)).toEqual(['b', 'c']);
  });

  it('ctrl+shift adds a range to the selection', () => {
    const s = run([{ type: 'click', id: 'a' }, { type: 'click', id: 'c', ctrl: true }, { type: 'click', id: 'e', shift: true, ctrl: true }]);
    expect(ids(s)).toEqual(['a', 'c', 'd', 'e']);
  });

  it('keyboard moves the cursor, shift extends, ctrl moves only the focus', () => {
    let s = run([{ type: 'move', delta: 1 }]);
    expect(s.focus).toBe('a');
    s = run([{ type: 'move', delta: 1, shift: true }, { type: 'move', delta: 1, shift: true }], s);
    expect(ids(s)).toEqual(['a', 'b', 'c']);
    s = run([{ type: 'move', delta: 1, ctrl: true }], s);
    expect(s.focus).toBe('d');
    expect(ids(s)).toEqual(['a', 'b', 'c']);
    s = run([{ type: 'move', delta: 10 }], s);
    expect(s.focus).toBe('e');
    expect(ids(s)).toEqual(['e']);
    s = run([{ type: 'moveTo', index: 0, shift: true }], s);
    expect(ids(s)).toEqual(['a', 'b', 'c', 'd', 'e']);
  });

  it('select all, clear, toggle, set and prune', () => {
    let s = run([{ type: 'selectAll' }]);
    expect(s.selected.size).toBe(5);
    s = run([{ type: 'clear' }], s);
    expect(s.selected.size).toBe(0);
    s = run([{ type: 'toggle', id: 'c' }, { type: 'toggle', id: 'd' }, { type: 'toggle', id: 'c' }], s);
    expect(ids(s)).toEqual(['d']);
    s = run([{ type: 'set', ids: ['a', 'zz'] }], s);
    expect(ids(s)).toEqual(['a']);
    const pruned = selectionReducer({ selected: new Set(['a', 'x']), anchor: 'x', focus: 'x' }, { type: 'prune' }, order);
    expect(ids(pruned)).toEqual(['a']);
    expect(pruned.focus).toBeNull();
  });
});

describe('navigation history', () => {
  it('pushes, goes back/forward/up and drops the forward entries', () => {
    let h = createHistory('/');
    h = pushPath(h, '/a');
    h = pushPath(h, '/a/b');
    expect(currentPath(h)).toBe('/a/b');
    h = goBack(h);
    expect(currentPath(h)).toBe('/a');
    expect(canGoForward(h)).toBe(true);
    h = goForward(h);
    expect(currentPath(h)).toBe('/a/b');
    h = goBack(goBack(h));
    expect(canGoBack(h)).toBe(false);
    h = pushPath(h, '/c');
    expect(canGoForward(h)).toBe(false);
    expect(h.entries).toEqual(['/', '/c']);
    h = goUp(pushPath(h, '/c/d'));
    expect(currentPath(h)).toBe('/c');
    expect(pushPath(h, '/c')).toBe(h);
  });

  it('syncs with an external path (browser back)', () => {
    let h = pushPath(pushPath(createHistory('/'), '/a'), '/a/b');
    h = syncPath(h, '/a');
    expect(h.index).toBe(1);
    h = syncPath(h, '/a/b');
    expect(h.index).toBe(2);
    h = syncPath(h, '/z');
    expect(h.entries).toEqual(['/', '/a', '/a/b', '/z']);
  });

  it('caps the history length', () => {
    let h = createHistory('/');
    for (let i = 0; i < 10; i++) h = pushPath(h, `/p${i}`, 5);
    expect(h.entries).toHaveLength(5);
    expect(currentPath(h)).toBe('/p9');
  });
});

describe('clipboard', () => {
  const folder = entry('/a', 'folder');
  const file = entry('/a/x.mp3');
  const clip = (mode: 'cut' | 'copy', entries: FileEntry[], sourcePath = '/'): ClipboardState => ({ mode, entries, providerId: 'p', sourcePath });
  const both = { move: true, copy: true };

  it('allows pasting into another folder of the same provider', () => {
    expect(pasteBlockReason(clip('cut', [file], '/a'), 'p', '/b', both)).toBeNull();
    expect(pasteBlockReason(clip('copy', [file], '/a'), 'p', '/a', both)).toBeNull();
  });

  it('blocks invalid pastes', () => {
    expect(pasteBlockReason(null, 'p', '/', both)).toBe('empty');
    expect(pasteBlockReason(clip('cut', [file]), 'other', '/b', both)).toBe('other-provider');
    expect(pasteBlockReason(clip('cut', [file], '/a'), 'p', '/a', both)).toBe('same-folder');
    expect(pasteBlockReason(clip('cut', [folder]), 'p', '/a/sub', both)).toBe('into-itself');
    expect(pasteBlockReason(clip('copy', [folder]), 'p', '/a', both)).toBe('into-itself');
    expect(pasteBlockReason(clip('copy', [file]), 'p', '/b', { move: true, copy: false })).toBe('unsupported');
  });
});

describe('keyboard', () => {
  it('maps the standard keys', () => {
    expect(resolveKey({ key: 'ArrowDown' })).toMatchObject({ type: 'move', delta: 1 });
    expect(resolveKey({ key: 'ArrowDown' }, { columns: 4 })).toMatchObject({ type: 'move', delta: 4 });
    expect(resolveKey({ key: 'ArrowLeft' })).toBeNull();
    expect(resolveKey({ key: 'ArrowLeft' }, { columns: 3 })).toMatchObject({ type: 'move', delta: -1 });
    expect(resolveKey({ key: 'ArrowUp', shiftKey: true })).toMatchObject({ type: 'move', delta: -1, shift: true });
    expect(resolveKey({ key: 'Enter' })).toEqual({ type: 'open' });
    expect(resolveKey({ key: 'Backspace' })).toEqual({ type: 'up' });
    expect(resolveKey({ key: 'F2' })).toEqual({ type: 'rename' });
    expect(resolveKey({ key: 'Delete' })).toEqual({ type: 'delete' });
    expect(resolveKey({ key: 'Escape' })).toEqual({ type: 'escape' });
    expect(resolveKey({ key: 'a', ctrlKey: true })).toEqual({ type: 'selectAll' });
    expect(resolveKey({ key: 'c', ctrlKey: true })).toEqual({ type: 'copy' });
    expect(resolveKey({ key: 'x', ctrlKey: true })).toEqual({ type: 'cut' });
    expect(resolveKey({ key: 'v', ctrlKey: true })).toEqual({ type: 'paste' });
    expect(resolveKey({ key: 'N', ctrlKey: true, shiftKey: true })).toEqual({ type: 'newFolder' });
    expect(resolveKey({ key: 'ArrowLeft', altKey: true })).toEqual({ type: 'back' });
    expect(resolveKey({ key: 'ArrowUp', altKey: true })).toEqual({ type: 'up' });
    expect(resolveKey({ key: 'm' })).toEqual({ type: 'typeAhead', char: 'm' });
  });

  it('uses Cmd on macOS', () => {
    expect(resolveKey({ key: 'a', metaKey: true }, { isMac: true })).toEqual({ type: 'selectAll' });
    expect(resolveKey({ key: 'a', ctrlKey: true }, { isMac: true })).toBeNull();
    expect(resolveKey({ key: 'Backspace', metaKey: true }, { isMac: true })).toEqual({ type: 'delete' });
  });

  it('matches and formats shortcuts', () => {
    expect(matchShortcut('mod+shift+d', { key: 'D', ctrlKey: true, shiftKey: true })).toBe(true);
    expect(matchShortcut('mod+shift+d', { key: 'd', ctrlKey: true })).toBe(false);
    expect(matchShortcut('mod+d', { key: 'd', metaKey: true }, true)).toBe(true);
    expect(matchShortcut('delete', { key: 'Delete' })).toBe(true);
    expect(formatShortcut('mod+shift+d')).toBe('Ctrl+Shift+D');
    expect(formatShortcut('mod+c', true)).toBe('⌘C');
  });

  it('type-ahead finds by prefix and cycles on a repeated letter', () => {
    const names = ['apple', 'banana', 'blueberry', 'cherry', 'beet'];
    const ta = createTypeAhead(500);
    expect(findTypeAhead(names, ta.push('b', 0), -1)).toBe(1);
    expect(findTypeAhead(names, ta.push('l', 100), 1)).toBe(2);
    const cycle = createTypeAhead(500);
    cycle.push('b', 0);
    expect(findTypeAhead(names, cycle.push('b', 100), 1)).toBe(2);
    expect(findTypeAhead(names, 'b', 2)).toBe(4);
    expect(findTypeAhead(names, 'b', 4)).toBe(1);
    // Timeout restarts the buffer
    expect(ta.push('c', 5000)).toBe('c');
    expect(findTypeAhead(names, 'zz', 0)).toBe(-1);
  });
});

describe('sort and filter', () => {
  const list = [entry('/b.mp3', 'file', { size: 5 }), entry('/a', 'folder'), entry('/c.jpg', 'file', { size: 1 }), entry('/A10.mp3', 'file', { size: 9 }), entry('/a2.mp3', 'file', { size: 3 })];

  it('keeps folders first and sorts naturally', () => {
    expect(sortEntries(list, { field: 'name', direction: 'asc' }).map((e) => e.name)).toEqual(['a', 'a2.mp3', 'A10.mp3', 'b.mp3', 'c.jpg']);
    expect(sortEntries(list, { field: 'size', direction: 'desc' }).map((e) => e.name)).toEqual(['a', 'A10.mp3', 'b.mp3', 'a2.mp3', 'c.jpg']);
  });

  it('filters by text and media kind (folders kept)', () => {
    expect(filterEntries(list, { mediaKinds: ['image'] }).map((e) => e.name)).toEqual(['a', 'c.jpg']);
    expect(filterEntries(list, { text: 'A1' }).map((e) => e.name)).toEqual(['A10.mp3']);
    expect(filterEntries(list, { kind: 'folder' }).map((e) => e.name)).toEqual(['a']);
  });
});

describe('actions registry', () => {
  const ctx = { provider: {} as never, currentPath: '/', entries: [], refresh() {}, navigate() {}, clearSelection() {} };
  it('filters by selection, placement and predicate, sorted by group/order', () => {
    expect(selectionMatches('single', 1)).toBe(true);
    expect(selectionMatches('single', 2)).toBe(false);
    expect(selectionMatches('multiple', 2)).toBe(true);
    expect(selectionMatches(undefined, 0)).toBe(false);
    expect(selectionMatches('none', 0)).toBe(true);
    const run = () => {};
    const actions = [
      { id: 'b', label: 'B', group: 'g2', run },
      { id: 'a', label: 'A', group: 'g1', order: 5, run },
      { id: 'a0', label: (e: FileEntry[]) => `A0 ${e.length}`, group: 'g1', order: 1, run },
      { id: 'single', label: 'S', selection: 'single' as const, run },
      { id: 'hidden', label: 'H', when: () => false, run },
      { id: 'toolbar', label: 'T', placement: ['toolbar' as const], run },
      { id: 'disabled', label: 'D', group: 'g3', isEnabled: () => false, run }
    ];
    const two = [entry('/x'), entry('/y')];
    const resolved = resolveActions(actions, 'context', two, ctx);
    expect(resolved.map((r) => r.action.id)).toEqual(['a0', 'a', 'b', 'disabled']);
    expect(resolved[0].label).toBe('A0 2');
    expect(resolved[3].enabled).toBe(false);
    expect(groupActions(resolved).map((g) => g.length)).toEqual([2, 1, 1]);
  });
});
