// @tfm/file-manager: reusable file manager. Headless core (pure modules + the
// useFileManager hook) and a default UI on @tfm/ui, fed by a FileSystemProvider.
// See README.md.

// Types of the provider adapter and the actions registry
export type {
  ActionPlacement,
  ActionSelection,
  EntryKind,
  FileAction,
  FileActionContext,
  FileEntry,
  FileSystemProvider,
  ListFilter,
  ListOptions,
  MediaKind,
  Page,
  ProviderCapabilities,
  SearchOptions,
  SortDirection,
  SortField,
  SortSpec,
  UploadOptions,
  UploadProgress,
  ViewMode
} from './types';

// Headless core (framework free)
export { baseName, extensionOf, isSameOrDescendant, isValidName, joinPath, normalizePath, parentPath, pathSegments } from './core/paths';
export { entryMediaKind, mediaKindOf } from './core/media';
export { compareEntries, filterEntries, sortEntries } from './core/sortFilter';
export { emptySelection, selectionReducer, type SelectionAction, type SelectionState } from './core/selection';
export {
  canGoBack,
  canGoForward,
  createHistory,
  currentPath,
  goBack,
  goForward,
  goUp,
  pushPath,
  syncPath,
  type HistoryState
} from './core/navigation';
export {
  createClipboardStore,
  globalClipboard,
  pasteBlockReason,
  type ClipboardMode,
  type ClipboardState,
  type ClipboardStore,
  type PasteBlockReason
} from './core/clipboard';
export {
  createTypeAhead,
  findTypeAhead,
  formatShortcut,
  isMacPlatform,
  matchShortcut,
  resolveKey,
  type FileManagerCommand,
  type KeyLike
} from './core/keyboard';
export { UploadQueue, type UploadItem, type UploadQueueOptions, type UploadStatus, type UploadTaskContext } from './core/uploadQueue';
export { groupActions, resolveActions, selectionMatches, type ResolvedAction } from './core/actions';

// Example / test provider
export { createMemoryProvider, type MemoryFile, type MemoryProviderOptions } from './providers/memoryProvider';

// React hooks
export { fileManagerQueryKey, useFileManager, type FileManagerController, type FileOperation, type UseFileManagerOptions } from './hooks/useFileManager';
export { useDebouncedValue } from './hooks/useDebouncedValue';

// Default UI
export { FileManager, FileManagerView, type FileManagerProps, type FileManagerViewProps } from './ui/FileManager';
export { defaultStrings, fmt, type FileManagerStrings } from './ui/strings';
export { EntryThumb, FileIcon } from './ui/FileIcon';
export { formatBytes, formatDate } from './ui/format';
export { useFileManagerUi, type FileManagerUi } from './ui/context';
