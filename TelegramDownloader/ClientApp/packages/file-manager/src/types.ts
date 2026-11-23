/**
 * Core types of @tfm/file-manager. The library knows nothing about TFM: every
 * data access goes through a {@link FileSystemProvider} (Telegram channel,
 * local files, shared collection, or anything else).
 */

export type EntryKind = 'folder' | 'file';

/** Broad media category, used for icons, previews and filters. */
export type MediaKind = 'audio' | 'video' | 'image' | 'document' | 'archive' | 'text' | 'other';

/** One item of a listing. */
export interface FileEntry {
  /** Stable id unique within the provider. */
  id: string;
  name: string;
  kind: EntryKind;
  /** Full path of the entry (folders end without slash; root is `/`). */
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
  /** Sort fields the provider can sort by server side. */
  sortFields?: SortField[];
}

export type SortField = 'name' | 'size' | 'modifiedAt' | 'createdAt' | 'type';
export type SortDirection = 'asc' | 'desc';

export interface SortSpec {
  field: SortField;
  direction: SortDirection;
}

export interface ListFilter {
  /** Restrict to these media kinds. */
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
  /** What to do when a file with the same name exists. */
  conflict?: 'rename' | 'overwrite' | 'skip';
}

/**
 * Data adapter: the only thing that changes between uses of the file manager.
 * Optional methods must be present when the matching capability is true.
 */
export interface FileSystemProvider {
  /** Stable id (used for caches and persisted view state), e.g. `channel:123`. */
  readonly id: string;
  /** Display name of the root (breadcrumb). */
  readonly rootName: string;
  readonly capabilities: ProviderCapabilities;

  list(path: string, options?: ListOptions): Promise<Page<FileEntry>>;
  /** Entry metadata (details panel); falls back to the listing when absent. */
  stat?(path: string, signal?: AbortSignal): Promise<FileEntry>;
  search?(path: string, query: string, options?: SearchOptions): Promise<Page<FileEntry>>;

  createFolder?(parentPath: string, name: string): Promise<FileEntry | void>;
  rename?(entry: FileEntry, newName: string): Promise<FileEntry | void>;
  move?(entries: FileEntry[], targetPath: string): Promise<void>;
  copy?(entries: FileEntry[], targetPath: string): Promise<void>;
  delete?(entries: FileEntry[]): Promise<void>;
  upload?(targetPath: string, files: File[], options?: UploadOptions): Promise<void>;
  /** Starts a download (browser download or server-side job). */
  download?(entries: FileEntry[]): Promise<void>;

  /** URL to preview/stream the entry (audio/video/image/pdf). */
  previewUrl?(entry: FileEntry): string | undefined;
  thumbnailUrl?(entry: FileEntry): string | undefined;
}

/**
 * App-specific extra actions shown in toolbars and context menus (in TFM:
 * download to server, preload, STRM, media URL, share, open in Telegram, add
 * to playlist...).
 */
export interface FileAction {
  id: string;
  label: string;
  icon?: unknown;
  /** Where the action appears. */
  placement?: ('toolbar' | 'context' | 'details')[];
  /** Keyboard shortcut, e.g. `mod+shift+d`. */
  shortcut?: string;
  destructive?: boolean;
  /** Whether the action applies to the current selection. */
  isEnabled?(entries: FileEntry[], context: FileActionContext): boolean;
  run(entries: FileEntry[], context: FileActionContext): void | Promise<void>;
}

export interface FileActionContext {
  provider: FileSystemProvider;
  currentPath: string;
  /** Re-fetch the current folder. */
  refresh(): void;
}

export type ViewMode = 'list' | 'grid' | 'details';
