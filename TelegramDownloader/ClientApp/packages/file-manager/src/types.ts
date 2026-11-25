/**
 * Core types of @tfm/file-manager. The library knows nothing about TFM: every
 * data access goes through a {@link FileSystemProvider} (Telegram channel,
 * local files, shared collection, or anything else).
 *
 * Paths: the library always uses absolute POSIX-like paths, root is `/`,
 * folders have no trailing slash (`/music/rock`). Providers translate them to
 * whatever their backend expects.
 */
import type { ReactNode } from 'react';

export type EntryKind = 'folder' | 'file';

/** Broad media category, used for icons, previews and filters. */
export type MediaKind = 'audio' | 'video' | 'image' | 'document' | 'archive' | 'text' | 'other';

/** One item of a listing. */
export interface FileEntry {
  /** Stable id unique within the provider. */
  id: string;
  name: string;
  kind: EntryKind;
  /** Full path of the entry (root is `/`, no trailing slash). */
  path: string;
  /** Path of the containing folder. */
  parentPath: string;
  /** Bytes; undefined for folders when unknown. */
  size?: number;
  /** ISO date. */
  modifiedAt?: string;
  createdAt?: string;
  mimeType?: string;
  /** Lower-case extension without dot. */
  extension?: string;
  mediaKind?: MediaKind;
  /** Number of children (folders), when the provider knows it. */
  childCount?: number;
  /** The folder has children (tree expanders). */
  hasChildren?: boolean;
  /** Provider-specific payload (e.g. the original API DTO). */
  data?: unknown;
}

/** Feature switches of a provider; the UI hides what is not supported. */
export interface ProviderCapabilities {
  rename: boolean;
  move: boolean;
  copy: boolean;
  delete: boolean;
  createFolder: boolean;
  upload: boolean;
  download: boolean;
  search: boolean;
  preview: boolean;
  thumbnails: boolean;
  /** The provider pages results server side (infinite scroll); otherwise `list` returns everything. */
  paging: boolean;
  /** Sort fields the provider sorts by server side (the others are sorted client side). */
  sortFields?: SortField[];
  /** The provider applies `filter.mediaKinds` / `filter.text` server side. */
  serverFilter?: boolean;
  /** Media kinds offered in the filter menu (default: audio, video, image, document, archive). */
  filterKinds?: MediaKind[];
}

export type SortField = 'name' | 'size' | 'modifiedAt' | 'createdAt' | 'type';
export type SortDirection = 'asc' | 'desc';

export interface SortSpec {
  field: SortField;
  direction: SortDirection;
}

export interface ListFilter {
  /** Restrict to these media kinds (folders are always kept). */
  mediaKinds?: MediaKind[];
  /** Free text filter of the current folder (client or server side). */
  text?: string;
  /** Only folders / only files. */
  kind?: EntryKind;
}

export interface ListOptions {
  /** 1-based page (ignored when `capabilities.paging` is false). */
  page?: number;
  pageSize?: number;
  sort?: SortSpec;
  filter?: ListFilter;
  signal?: AbortSignal;
}

/** A page of entries. */
export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  hasNext: boolean;
}

export interface SearchOptions extends Omit<ListOptions, 'filter'> {
  /** Search inside sub-folders too (default true). */
  recursive?: boolean;
  filter?: Omit<ListFilter, 'text'>;
}

/** Upload progress of one file. */
export interface UploadProgress {
  file: File;
  /** Bytes sent. */
  loaded: number;
  total: number;
}

export interface UploadOptions {
  onProgress?: (progress: UploadProgress) => void;
  signal?: AbortSignal;
}

/**
 * Data adapter: the only thing that changes between uses of the file manager.
 * Optional methods must be present when the matching capability is true.
 */
export interface FileSystemProvider {
  /** Stable id (used for caches, the clipboard and drag & drop), e.g. `channel:123`. */
  readonly id: string;
  /** Display name of the root (breadcrumb). */
  readonly rootName: string;
  readonly capabilities: ProviderCapabilities;

  list(path: string, options?: ListOptions): Promise<Page<FileEntry>>;
  /** Entry metadata (details panel); the listing entry is used when absent. */
  stat?(entry: FileEntry, signal?: AbortSignal): Promise<FileEntry>;
  /** Recursive search under `path`. Without it, the search box filters the current folder. */
  search?(path: string, query: string, options?: SearchOptions): Promise<Page<FileEntry>>;

  createFolder?(parentPath: string, name: string): Promise<FileEntry | void>;
  rename?(entry: FileEntry, newName: string): Promise<FileEntry | void>;
  move?(entries: FileEntry[], targetPath: string): Promise<void>;
  copy?(entries: FileEntry[], targetPath: string): Promise<void>;
  delete?(entries: FileEntry[]): Promise<void>;
  /** Uploads ONE file (the upload queue calls it per file, with progress and cancel). */
  upload?(targetPath: string, file: File, options?: UploadOptions): Promise<void>;
  /** Downloads entries (browser download or server-side job). */
  download?(entries: FileEntry[]): Promise<void>;

  /** URL to preview/stream the entry (audio/video/image/pdf). */
  previewUrl?(entry: FileEntry): string | undefined;
  thumbnailUrl?(entry: FileEntry): string | undefined;
}

/** How many selected entries an action needs. */
export type ActionSelection = 'none' | 'single' | 'multiple' | 'any';

/** Where an action appears. */
export type ActionPlacement = 'toolbar' | 'context' | 'details' | 'selection';

/**
 * App-specific extra actions shown in the toolbar, context menu, details panel
 * and selection bar (in TFM: download to server, preload, STRM, media URL,
 * share, open in Telegram, add to playlist...). Equivalent to the `Can*` flags
 * + events of the Blazor MobileFileManager.
 */
export interface FileAction {
  id: string;
  label: string | ((entries: FileEntry[]) => string);
  icon?: ReactNode;
  /** Where the action appears (default: context + details + selection; `none` actions: toolbar + context). */
  placement?: ActionPlacement[];
  /**
   * Selection it applies to: `single` (exactly one), `multiple` (two or more),
   * `any` (one or more, the default) or `none` (works on the current folder:
   * toolbar and background context menu; receives an empty array).
   */
  selection?: ActionSelection;
  /** Actions are separated by group in menus. */
  group?: string;
  /** Sort key inside the menus (default 100). */
  order?: number;
  /** Keyboard shortcut, e.g. `mod+shift+d` (`mod` = Ctrl, or Cmd on macOS). */
  shortcut?: string;
  destructive?: boolean;
  /** Visibility predicate (hidden when false). */
  when?(entries: FileEntry[], context: FileActionContext): boolean;
  /** Shown but disabled when false. */
  isEnabled?(entries: FileEntry[], context: FileActionContext): boolean;
  run(entries: FileEntry[], context: FileActionContext): void | Promise<void>;
}

export interface FileActionContext {
  provider: FileSystemProvider;
  currentPath: string;
  /** Entries of the current listing (loaded pages, sorted/filtered as shown). */
  entries: FileEntry[];
  /** Re-fetch the current folder. */
  refresh(): void;
  navigate(path: string): void;
  clearSelection(): void;
}

export type ViewMode = 'list' | 'grid' | 'details';
