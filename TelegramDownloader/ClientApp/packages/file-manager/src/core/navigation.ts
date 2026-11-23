import { normalizePath, parentPath } from './paths';

/** Navigation history (pure): a list of visited paths and the current index. */
export interface HistoryState {
  entries: readonly string[];
  index: number;
}

export function createHistory(initialPath = '/'): HistoryState {
  return { entries: [normalizePath(initialPath)], index: 0 };
}

export function currentPath(h: HistoryState): string {
  return h.entries[h.index] ?? '/';
}

/** Goes to `path`, dropping the forward entries. Same path = no change. */
export function pushPath(h: HistoryState, path: string, maxEntries = 100): HistoryState {
  const p = normalizePath(path);
  if (p === currentPath(h)) return h;
  const entries = [...h.entries.slice(0, h.index + 1), p];
  const overflow = Math.max(0, entries.length - maxEntries);
  return { entries: entries.slice(overflow), index: entries.length - 1 - overflow };
}

export const canGoBack = (h: HistoryState) => h.index > 0;
export const canGoForward = (h: HistoryState) => h.index < h.entries.length - 1;

export function goBack(h: HistoryState): HistoryState {
  return canGoBack(h) ? { ...h, index: h.index - 1 } : h;
}

export function goForward(h: HistoryState): HistoryState {
  return canGoForward(h) ? { ...h, index: h.index + 1 } : h;
}

export function goUp(h: HistoryState): HistoryState {
  return pushPath(h, parentPath(currentPath(h)));
}

/**
 * Syncs the history with a path that changed outside (router, browser back):
 * matching the previous/next entry moves the index, anything else is pushed.
 */
export function syncPath(h: HistoryState, path: string): HistoryState {
  const p = normalizePath(path);
  if (p === currentPath(h)) return h;
  if (h.entries[h.index - 1] === p) return { ...h, index: h.index - 1 };
  if (h.entries[h.index + 1] === p) return { ...h, index: h.index + 1 };
  return pushPath(h, p);
}
