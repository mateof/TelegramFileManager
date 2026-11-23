import type { FileEntry } from '../types';
import { isSameOrDescendant, normalizePath } from './paths';

export type ClipboardMode = 'cut' | 'copy';

/** Entries cut or copied in a file manager, waiting for a paste. */
export interface ClipboardState {
  mode: ClipboardMode;
  entries: readonly FileEntry[];
  /** Provider the entries belong to (paste only works inside the same provider). */
  providerId: string;
  /** Folder they were taken from. */
  sourcePath: string;
}

export type PasteBlockReason = 'empty' | 'other-provider' | 'same-folder' | 'into-itself' | 'unsupported';

/** Why a paste into `targetPath` is not possible, or null when it is. */
export function pasteBlockReason(
  clip: ClipboardState | null,
  providerId: string,
  targetPath: string,
  supports: { move: boolean; copy: boolean }
): PasteBlockReason | null {
  if (!clip || clip.entries.length === 0) return 'empty';
  if (clip.providerId !== providerId) return 'other-provider';
  if (clip.mode === 'cut' ? !supports.move : !supports.copy) return 'unsupported';
  const target = normalizePath(targetPath);
  if (clip.mode === 'cut' && normalizePath(clip.sourcePath) === target) return 'same-folder';
  if (clip.entries.some((e) => e.kind === 'folder' && isSameOrDescendant(target, e.path))) return 'into-itself';
  return null;
}

type Listener = () => void;

/**
 * A tiny clipboard store. One global instance is shared by every file manager
 * of the page (`globalClipboard`), so cutting in one view and pasting in
 * another view of the same provider works.
 */
export function createClipboardStore() {
  let state: ClipboardState | null = null;
  const listeners = new Set<Listener>();
  const emit = () => listeners.forEach((l) => l());
  return {
    get: () => state,
    set(next: ClipboardState | null) {
      state = next;
      emit();
    },
    clear() {
      if (state) {
        state = null;
        emit();
      }
    },
    subscribe(listener: Listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    }
  };
}

export type ClipboardStore = ReturnType<typeof createClipboardStore>;

export const globalClipboard: ClipboardStore = createClipboardStore();
