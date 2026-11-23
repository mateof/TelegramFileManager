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
