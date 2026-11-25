import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore, type KeyboardEvent } from 'react';
import { globalClipboard, pasteBlockReason, type ClipboardMode, type ClipboardState, type ClipboardStore, type PasteBlockReason } from '../core/clipboard';
import { createTypeAhead, findTypeAhead, isMacPlatform, resolveKey, type FileManagerCommand } from '../core/keyboard';
import {
  canGoBack,
  canGoForward,
  createHistory,
  currentPath,
  goBack,
  goForward,
  pushPath,
  syncPath,
  type HistoryState
} from '../core/navigation';
import { isValidName, normalizePath, parentPath, pathSegments } from '../core/paths';
import { emptySelection, selectionReducer, type SelectionAction, type SelectionState } from '../core/selection';
import { filterEntries, sortEntries } from '../core/sortFilter';
import { UploadQueue, type UploadItem } from '../core/uploadQueue';
import type { FileActionContext, FileEntry, FileSystemProvider, ListFilter, Page, SortSpec, ViewMode } from '../types';
import { useDebouncedValue } from './useDebouncedValue';

/** Operations reported to `onError` / `onSuccess`. */
export type FileOperation = 'list' | 'createFolder' | 'rename' | 'move' | 'copy' | 'delete' | 'upload' | 'download';

export interface UseFileManagerOptions {
  provider: FileSystemProvider;
  /** Controlled current path (e.g. from the router). Pair it with `onPathChange`. */
  path?: string;
  /** Start path when uncontrolled (default `/`). */
  initialPath?: string;
  /** Called on every navigation (folder open, breadcrumb, back/forward/up). */
  onPathChange?(path: string): void;
  /** Page size asked to paging providers (default 100). */
  pageSize?: number;
  initialSort?: SortSpec;
  initialFilter?: ListFilter;
  initialViewMode?: ViewMode;
  /** Debounce of the search box (default 300 ms). */
  searchDebounceMs?: number;
  /** Parallel uploads (default 2). */
  uploadConcurrency?: number;
  /** Clipboard store (default: one shared by every file manager of the page). */
  clipboard?: ClipboardStore;
  /**
   * A file was opened (double click, Enter, tap). Return `true` when handled;
   * otherwise the preview URL is opened in a new tab when the provider has one.
   */
  onOpen?(entry: FileEntry, fm: FileManagerController): boolean | void;
  /** Ask before deleting (default: `window.confirm`). */
  confirmDelete?(entries: FileEntry[]): Promise<boolean> | boolean;
  onError?(error: unknown, operation: FileOperation): void;
  onSuccess?(operation: FileOperation, entries: FileEntry[]): void;
  onSelectionChange?(entries: FileEntry[]): void;
}

export interface FileManagerController {
  provider: FileSystemProvider;
  /** Current folder. */
  path: string;
  /** Root + one item per segment of `path`. */
  breadcrumbs: { name: string; path: string }[];
  navigate(path: string): void;
  back(): void;
  forward(): void;
  up(): void;
  canBack: boolean;
  canForward: boolean;
  canUp: boolean;

  /** What is shown: loaded pages, filtered and sorted. */
  entries: FileEntry[];
  /** Total of the folder as reported by the provider (all pages). */
  totalItems: number;
  isLoading: boolean;
  isFetching: boolean;
  error: unknown;
  hasNextPage: boolean;
  isFetchingNextPage: boolean;
  fetchNextPage(): void;
  refresh(): void;

  sort: SortSpec;
  setSort(sort: SortSpec): void;
  /** Sets the field; clicking the same field again flips the direction. */
  toggleSort(field: SortSpec['field']): void;
  filter: ListFilter;
  setFilter(filter: ListFilter): void;
  /** Search box text (raw) and whether a search is active (debounced, not empty). */
  search: string;
  setSearch(text: string): void;
  isSearching: boolean;
  viewMode: ViewMode;
  setViewMode(mode: ViewMode): void;
  /** Columns of the grid (set by the grid view, used by the arrow keys). */
  columns: number;
  setColumns(columns: number): void;

  selection: {
    ids: ReadonlySet<string>;
    entries: FileEntry[];
    focusId: string | null;
    isSelected(id: string): boolean;
    dispatch(action: SelectionAction): void;
    /** Touch selection mode (after a long press): taps toggle instead of opening. */
    selectionMode: boolean;
    setSelectionMode(on: boolean): void;
  };

  clipboard: {
    state: ClipboardState | null;
    cut(entries: FileEntry[]): void;
    copy(entries: FileEntry[]): void;
    paste(targetPath?: string): Promise<void>;
    /** Null when a paste into the current folder is possible. */
    blockReason: PasteBlockReason | null;
    isCut(id: string): boolean;
    clear(): void;
  };

  rename: {
    editingId: string | null;
    start(entry: FileEntry): void;
    cancel(): void;
    commit(newName: string): Promise<void>;
  };
  createFolder(name: string): Promise<void>;
  deleteEntries(entries: FileEntry[]): Promise<void>;
  moveEntries(entries: FileEntry[], targetPath: string, mode?: ClipboardMode): Promise<void>;
  download(entries: FileEntry[]): Promise<void>;
  /** Folder → navigate; file → `onOpen` / preview URL. */
  open(entry: FileEntry): void;
  /** True while a mutation runs. */
  busy: boolean;

  uploads: {
    items: readonly UploadItem[];
    add(files: File[], targetPath?: string): void;
    cancel(id: string): void;
    retry(id: string): void;
    remove(id: string): void;
    clearFinished(): void;
    cancelAll(): void;
  };

  /** Context passed to custom actions. */
  actionContext: FileActionContext;
  /** Keyboard handler of the item area (arrows, Enter, F2, Delete, Ctrl+A/C/X/V, type-ahead...). */
  handleKeyDown(event: KeyboardEvent<HTMLElement>): void;
  /** The search box requested the focus (Ctrl+F). Views/toolbars listen to it. */
  focusSearchToken: number;
  /** The new folder prompt was requested (Ctrl+Shift+N). */
  newFolderToken: number;
}

const DEFAULT_SORT: SortSpec = { field: 'name', direction: 'asc' };
const PAGE_JUMP = 10;

/** Cache key prefix of the listings of a provider (to invalidate them from outside). */
export function fileManagerQueryKey(providerId: string): readonly unknown[] {
  return ['tfm-file-manager', providerId];
}

function useLatest<T>(value: T) {
  const ref = useRef(value);
  useEffect(() => {
    ref.current = value;
  });
  return ref;
}

/**
 * Headless file manager: navigation with history, listing (infinite pages),
 * sort, filters, debounced search, selection, clipboard, rename, create,
 * delete, move/copy, uploads and the keyboard. Requires a TanStack Query
 * `QueryClientProvider` above it.
 */
export function useFileManager(options: UseFileManagerOptions): FileManagerController {
  const { provider } = options;
  const caps = provider.capabilities;
  const queryClient = useQueryClient();
  const optionsRef = useLatest(options);

  // ---- navigation -------------------------------------------------------
  const [history, setHistory] = useState<HistoryState>(() => createHistory(options.path ?? options.initialPath ?? '/'));
  const [syncedPath, setSyncedPath] = useState(options.path);
  if (options.path !== undefined && options.path !== syncedPath) {
    // Adjust the history while rendering when the controlled path changes outside (router, browser back)
    setSyncedPath(options.path);
    setHistory((h) => syncPath(h, options.path ?? '/'));
  }
  const path = options.path !== undefined ? normalizePath(options.path) : currentPath(history);

  const applyHistory = useCallback(
    (next: HistoryState) => {
      setHistory(next);
      optionsRef.current.onPathChange?.(currentPath(next));
    },
    [optionsRef]
  );
  const navigate = useCallback((p: string) => applyHistory(pushPath(syncPath(history, path), p)), [applyHistory, history, path]);
  const back = useCallback(() => applyHistory(goBack(syncPath(history, path))), [applyHistory, history, path]);
  const forward = useCallback(() => applyHistory(goForward(syncPath(history, path))), [applyHistory, history, path]);
  const up = useCallback(() => navigate(parentPath(path)), [navigate, path]);

  // ---- listing ----------------------------------------------------------
  const [sort, setSort] = useState<SortSpec>(options.initialSort ?? DEFAULT_SORT);
  const [filter, setFilter] = useState<ListFilter>(options.initialFilter ?? {});
  const [search, setSearch] = useState('');
  const [viewMode, setViewMode] = useState<ViewMode>(options.initialViewMode ?? 'details');
  const [columns, setColumns] = useState(1);
  const debouncedSearch = useDebouncedValue(search.trim(), options.searchDebounceMs ?? 300);
  const searchMode = debouncedSearch.length > 0 && caps.search && !!provider.search;
  const pageSize = options.pageSize ?? 100;
  const serverSort = caps.paging && (caps.sortFields ?? []).includes(sort.field);
  const listFilter: ListFilter = useMemo(
    () => ({ ...filter, text: searchMode ? undefined : debouncedSearch || undefined }),
    [filter, searchMode, debouncedSearch]
  );

  const query = useInfiniteQuery({
    queryKey: [
      ...fileManagerQueryKey(provider.id),
      path,
      searchMode ? debouncedSearch : null,
      serverSort ? sort : null,
      caps.serverFilter ? listFilter : null,
      pageSize
    ],
    queryFn: ({ pageParam, signal }): Promise<Page<FileEntry>> => {
      const common = { page: pageParam, pageSize, sort, signal };
      if (searchMode && provider.search) {
        return provider.search(path, debouncedSearch, { ...common, filter: { mediaKinds: filter.mediaKinds, kind: filter.kind } });
      }
      return provider.list(path, { ...common, filter: listFilter });
    },
    initialPageParam: 1,
    getNextPageParam: (last) => (caps.paging && last.hasNext ? last.page + 1 : undefined),
    placeholderData: (previous, previousQuery) =>
      // Keep the previous items while re-sorting/filtering the same folder (no flash), not across folders
      previousQuery?.queryKey[1] === provider.id && previousQuery.queryKey[2] === path ? previous : undefined
  });

  const entries = useMemo(() => {
    const raw = query.data?.pages.flatMap((p) => p.items) ?? [];
    // De-duplicate (a page boundary can move while new files arrive)
    const seen = new Set<string>();
    const unique = raw.filter((e) => (seen.has(e.id) ? false : (seen.add(e.id), true)));
    const filtered = filterEntries(unique, searchMode ? { mediaKinds: filter.mediaKinds, kind: filter.kind } : listFilter);
    return serverSort ? filtered : sortEntries(filtered, sort);
  }, [query.data, searchMode, filter, listFilter, serverSort, sort]);
  const totalItems = query.data?.pages[0]?.totalItems ?? entries.length;
  const order = useMemo(() => entries.map((e) => e.id), [entries]);

  const { refetch } = query;
  const refresh = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: fileManagerQueryKey(provider.id) });
  }, [queryClient, provider.id]);
  // An explicit refresh of what is shown (F5) also refetches when the cache is fresh
  const hardRefresh = useCallback(() => {
    void refetch();
  }, [refetch]);

  // ---- selection (reset on every folder / search change) --------------------
  const viewKey = `${provider.id}|${path}|${searchMode ? debouncedSearch : ''}`;
  const [selectionState, setSelectionState] = useState<{ key: string; sel: SelectionState }>({ key: viewKey, sel: emptySelection });
  const sel = selectionState.key === viewKey ? selectionState.sel : emptySelection;
  const [selectionMode, setSelectionMode] = useState(false);
  const dispatch = useCallback(
    (action: SelectionAction) =>
      setSelectionState((s) => ({ key: viewKey, sel: selectionReducer(s.key === viewKey ? s.sel : emptySelection, action, order) })),
    [viewKey, order]
  );
  const selectedEntries = useMemo(() => entries.filter((e) => sel.selected.has(e.id)), [entries, sel.selected]);
  const selectedIdsKey = selectedEntries.map((e) => e.id).join('|');
  const lastSelectionKey = useRef('');
  useEffect(() => {
    if (lastSelectionKey.current === selectedIdsKey) return;
    lastSelectionKey.current = selectedIdsKey;
    optionsRef.current.onSelectionChange?.(selectedEntries);
  }, [selectedIdsKey, selectedEntries, optionsRef]);
  const effectiveSelectionMode = selectionMode && selectedEntries.length > 0;

  // ---- mutations --------------------------------------------------------
  const [busyCount, setBusyCount] = useState(0);
  const run = useCallback(
    async (operation: FileOperation, affected: FileEntry[], work: () => Promise<unknown>) => {
      setBusyCount((n) => n + 1);
      try {
        await work();
        optionsRef.current.onSuccess?.(operation, affected);
      } catch (error) {
        optionsRef.current.onError?.(error, operation);
        throw error;
      } finally {
        setBusyCount((n) => n - 1);
        refresh();
      }
    },
    [optionsRef, refresh]
  );
  const quiet = (p: Promise<void>) => p.catch(() => undefined);

  // ---- clipboard --------------------------------------------------------
  const clipboardStore = options.clipboard ?? globalClipboard;
  const clip = useSyncExternalStore(clipboardStore.subscribe, clipboardStore.get, clipboardStore.get);
  const blockReason = pasteBlockReason(clip, provider.id, path, { move: caps.move && !!provider.move, copy: caps.copy && !!provider.copy });
  const cutIds = useMemo(() => new Set(clip?.mode === 'cut' && clip.providerId === provider.id ? clip.entries.map((e) => e.id) : []), [clip, provider.id]);

  const moveEntries = useCallback(
    (list: FileEntry[], targetPath: string, mode: ClipboardMode = 'cut') => {
      const target = normalizePath(targetPath);
      const op: FileOperation = mode === 'cut' ? 'move' : 'copy';
      const fn = mode === 'cut' ? provider.move : provider.copy;
      if (!fn || list.length === 0) return Promise.resolve();
      return quiet(run(op, list, () => fn.call(provider, list, target)));
    },
    [provider, run]
  );

  const paste = useCallback(
    async (targetPath?: string) => {
      const target = normalizePath(targetPath ?? path);
      const reason = pasteBlockReason(clip, provider.id, target, { move: caps.move && !!provider.move, copy: caps.copy && !!provider.copy });
      if (reason || !clip) return;
      await moveEntries([...clip.entries], target, clip.mode);
      if (clip.mode === 'cut') clipboardStore.clear();
    },
    [clip, provider, caps.move, caps.copy, path, moveEntries, clipboardStore]
  );

  // ---- rename / create / delete / download ------------------------------
  const [editingId, setEditingId] = useState<string | null>(null);
  const renameCommit = useCallback(
    async (newName: string) => {
      const entry = entries.find((e) => e.id === editingId);
      setEditingId(null);
      const name = newName.trim();
      if (!entry || !provider.rename || name === entry.name || !isValidName(name)) return;
      await quiet(run('rename', [entry], () => provider.rename!(entry, name)));
    },
    [entries, editingId, provider, run]
  );

  const createFolder = useCallback(
    async (name: string) => {
      const n = name.trim();
      if (!provider.createFolder || !isValidName(n)) return;
      await run('createFolder', [], () => provider.createFolder!(path, n));
    },
    [provider, path, run]
  );

  const deleteEntries = useCallback(
    async (list: FileEntry[]) => {
      if (!provider.delete || list.length === 0) return;
      const ask = optionsRef.current.confirmDelete;
      const ok = ask ? await ask(list) : typeof window !== 'undefined' && window.confirm(`Delete ${list.length} item(s)?`);
      if (!ok) return;
      await quiet(run('delete', list, () => provider.delete!(list)));
      dispatch({ type: 'clear' });
    },
    [provider, optionsRef, run, dispatch]
  );

  const download = useCallback(
    async (list: FileEntry[]) => {
      if (!provider.download || list.length === 0) return;
      try {
        await provider.download(list);
        optionsRef.current.onSuccess?.('download', list);
      } catch (error) {
        optionsRef.current.onError?.(error, 'download');
      }
    },
    [provider, optionsRef]
  );

  // ---- uploads ----------------------------------------------------------
  const [queue] = useState(() => new UploadQueue({ concurrency: options.uploadConcurrency ?? 2 }));
  useEffect(
    () =>
      queue.onSettled((item) => {
        if (item.status === 'error') optionsRef.current.onError?.(new Error(item.error ?? 'Upload failed'), 'upload');
        void queryClient.invalidateQueries({ queryKey: fileManagerQueryKey(item.providerId ?? '') });
      }),
    [queue, queryClient, optionsRef]
  );
  const uploadItems = useSyncExternalStore(queue.subscribe, queue.getSnapshot, queue.getSnapshot);
  const addUploads = useCallback(
    (files: File[], targetPath?: string) => {
      const upload = provider.upload;
      if (!caps.upload || !upload || files.length === 0) return;
      // Each item keeps its own provider: navigating away does not redirect running uploads
      queue.add(files, normalizePath(targetPath ?? path), provider.id, (item, ctx) =>
        upload.call(provider, item.targetPath, item.file, { signal: ctx.signal, onProgress: (p) => ctx.onProgress(p.loaded, p.total) })
      );
    },
    [caps.upload, provider, queue, path]
  );

  // ---- open -------------------------------------------------------------
  const controllerRef = useRef<FileManagerController | null>(null);
  const open = useCallback(
    (entry: FileEntry) => {
      if (entry.kind === 'folder') {
        navigate(entry.path);
        return;
      }
      const handled = controllerRef.current && optionsRef.current.onOpen?.(entry, controllerRef.current);
      if (handled) return;
      const url = provider.previewUrl?.(entry);
      if (url && typeof window !== 'undefined') window.open(url, '_blank', 'noopener');
    },
    [navigate, optionsRef, provider]
  );

  // ---- keyboard ---------------------------------------------------------
  const [typeAhead] = useState(() => createTypeAhead());
  const [focusSearchToken, setFocusSearchToken] = useState(0);
  const [newFolderToken, setNewFolderToken] = useState(0);
  const [isMac] = useState(isMacPlatform);

  const execute = useCallback(
    (cmd: FileManagerCommand): boolean => {
      const focusIndex = sel.focus ? order.indexOf(sel.focus) : -1;
      const targets = selectedEntries.length ? selectedEntries : entries.filter((e) => e.id === sel.focus);
      switch (cmd.type) {
        case 'move':
          dispatch({ type: 'move', delta: cmd.delta, shift: cmd.shift, ctrl: cmd.ctrl });
          return true;
        case 'moveTo':
          dispatch({ type: 'moveTo', index: cmd.to === 'start' ? 0 : order.length - 1, shift: cmd.shift });
          return true;
        case 'page':
          dispatch({ type: 'moveTo', index: Math.max(0, focusIndex) + cmd.direction * PAGE_JUMP * Math.max(1, columns), shift: cmd.shift });
          return true;
        case 'open': {
          const target = entries.find((e) => e.id === sel.focus) ?? selectedEntries[0];
          if (target) open(target);
          return !!target;
        }
        case 'up':
          if (path === '/') return false;
          up();
          return true;
        case 'back':
          back();
          return true;
        case 'forward':
          forward();
          return true;
        case 'rename': {
          const target = targets.length === 1 ? targets[0] : null;
          if (!target || !caps.rename || !provider.rename) return false;
          setEditingId(target.id);
          return true;
        }
        case 'delete':
          if (!caps.delete || targets.length === 0) return false;
          void deleteEntries(targets);
          return true;
        case 'selectAll':
          dispatch({ type: 'selectAll' });
          return true;
        case 'copy':
        case 'cut': {
          const allowed = cmd.type === 'cut' ? caps.move : caps.copy;
          if (!allowed || targets.length === 0) return false;
          clipboardStore.set({ mode: cmd.type, entries: targets, providerId: provider.id, sourcePath: path });
          return true;
        }
        case 'paste':
          if (blockReason) return false;
          void paste();
          return true;
        case 'escape':
          if (editingId) setEditingId(null);
          else if (selectedEntries.length) dispatch({ type: 'clear' });
          else if (search) setSearch('');
          else return false;
          setSelectionMode(false);
          return true;
        case 'newFolder':
          if (!caps.createFolder) return false;
          setNewFolderToken((n) => n + 1);
          return true;
        case 'refresh':
          hardRefresh();
          return true;
        case 'search':
          setFocusSearchToken((n) => n + 1);
          return true;
        case 'toggleSelection':
          if (!sel.focus) return false;
          dispatch({ type: 'toggle', id: sel.focus });
          return true;
        case 'typeAhead': {
          const prefix = typeAhead.push(cmd.char);
          const idx = findTypeAhead(
            entries.map((e) => e.name),
            prefix,
            focusIndex
          );
          if (idx >= 0) dispatch({ type: 'moveTo', index: idx });
          return idx >= 0;
        }
      }
    },
    [
      sel.focus,
      order,
      selectedEntries,
      entries,
      dispatch,
      columns,
      open,
      path,
      up,
      back,
      forward,
      caps,
      provider,
      deleteEntries,
      clipboardStore,
      blockReason,
      paste,
      editingId,
      search,
      hardRefresh,
      typeAhead
    ]
  );

  const handleKeyDown = useCallback(
    (event: KeyboardEvent<HTMLElement>) => {
      const target = event.target as HTMLElement;
      if (target.closest('input, textarea, select, [contenteditable="true"]')) return;
      const cmd = resolveKey(event, { columns: viewMode === 'grid' ? columns : 1, isMac });
      if (cmd && execute(cmd)) {
        event.preventDefault();
        event.stopPropagation();
      }
    },
    [viewMode, columns, isMac, execute]
  );

  const actionContext = useMemo<FileActionContext>(
    () => ({ provider, currentPath: path, entries, refresh, navigate, clearSelection: () => dispatch({ type: 'clear' }) }),
    [provider, path, entries, refresh, navigate, dispatch]
  );

  const breadcrumbs = useMemo(() => [{ name: provider.rootName, path: '/' }, ...pathSegments(path)], [provider.rootName, path]);

  const controller: FileManagerController = {
    provider,
    path,
    breadcrumbs,
    navigate,
    back,
    forward,
    up,
    canBack: canGoBack(syncPath(history, path)),
    canForward: canGoForward(syncPath(history, path)),
    canUp: path !== '/',

    entries,
    totalItems,
    isLoading: query.isLoading,
    isFetching: query.isFetching,
    error: query.error,
    hasNextPage: !!query.hasNextPage,
    isFetchingNextPage: query.isFetchingNextPage,
    fetchNextPage: () => {
      if (query.hasNextPage && !query.isFetchingNextPage) void query.fetchNextPage();
    },
    refresh: hardRefresh,

    sort,
    setSort,
    toggleSort: (field) =>
      setSort((s) => (s.field === field ? { field, direction: s.direction === 'asc' ? 'desc' : 'asc' } : { field, direction: field === 'name' || field === 'type' ? 'asc' : 'desc' })),
    filter,
    setFilter,
    search,
    setSearch,
    isSearching: debouncedSearch.length > 0,
    viewMode,
    setViewMode,
    columns,
    setColumns,

    selection: {
      ids: sel.selected,
      entries: selectedEntries,
      focusId: sel.focus,
      isSelected: (id) => sel.selected.has(id),
      dispatch,
      selectionMode: effectiveSelectionMode,
      setSelectionMode
    },

    clipboard: {
      state: clip,
      cut: (list) => list.length && clipboardStore.set({ mode: 'cut', entries: list, providerId: provider.id, sourcePath: path }),
      copy: (list) => list.length && clipboardStore.set({ mode: 'copy', entries: list, providerId: provider.id, sourcePath: path }),
      paste,
      blockReason,
      isCut: (id) => cutIds.has(id),
      clear: () => clipboardStore.clear()
    },

    rename: {
      editingId,
      start: (entry) => caps.rename && setEditingId(entry.id),
      cancel: () => setEditingId(null),
      commit: renameCommit
    },
    createFolder,
    deleteEntries,
    moveEntries,
    download,
    open,
    busy: busyCount > 0,

    uploads: {
      items: uploadItems,
      add: addUploads,
      cancel: (id) => queue.cancel(id),
      retry: (id) => queue.retry(id),
      remove: (id) => queue.remove(id),
      clearFinished: () => queue.clearFinished(),
      cancelAll: () => queue.cancelAll()
    },

    actionContext,
    handleKeyDown,
    focusSearchToken,
    newFolderToken
  };

  useEffect(() => {
    controllerRef.current = controller;
  });

  return controller;
}
