import { createContext, useContext } from 'react';
import type { FileManagerController } from '../hooks/useFileManager';
import type { FileAction, FileEntry } from '../types';
import type { FileManagerStrings } from './strings';

export interface FileManagerUi {
  fm: FileManagerController;
  strings: FileManagerStrings;
  /** Built-in + app actions. */
  actions: FileAction[];
  locale?: string;
  isMobile: boolean;
  /** Show thumbnails (grid always tries; list/details show small ones when true). */
  thumbnails: boolean;
  /** Mouse: a plain click on a folder opens it (files still select on click, open on double click). */
  singleClickFolders: boolean;
  /**
   * Remembers the entry of a first mouse click (`opened`: a folder opened by that click). The rest of a
   * double click can land on another row (the selection bar shifts the list, or a folder was opened),
   * so the double click acts on this entry instead.
   */
  rememberClick(entry: FileEntry, opened: boolean): void;
  /** The click remembered less than `ms` ago, if any. */
  recentClick(ms: number): { entry: FileEntry; opened: boolean } | null;
  openDetails(entry?: FileEntry): void;
  requestNewFolder(): void;
  requestUpload(targetPath?: string): void;
  /** Entries being dragged inside this page (internal drag & drop). */
  dragging: FileEntry[] | null;
  setDragging(entries: FileEntry[] | null): void;
  /** Folder id highlighted as a drop target. */
  dropTargetId: string | null;
  setDropTargetId(id: string | null): void;
}

export const FileManagerUiContext = createContext<FileManagerUi | null>(null);

export function useFileManagerUi(): FileManagerUi {
  const ctx = useContext(FileManagerUiContext);
  if (!ctx) throw new Error('File manager components must be rendered inside <FileManager>');
  return ctx;
}

/** MIME type of internal drags (entries of a provider). */
export const DRAG_MIME = 'application/x-tfm-file-manager';
