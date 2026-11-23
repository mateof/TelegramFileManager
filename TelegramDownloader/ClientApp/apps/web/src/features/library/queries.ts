import type { ApiLibraryScanStateDto, LibraryFileStatus, LibraryItemsQuery, LibraryKind } from '@tfm/api-client';
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from '@tfm/ui';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { useEventsMessage } from '@/lib/realtime';

/** Private query keys of the library feature. */
export const libraryKeys = {
  all: ['library'] as const,
  stats: ['library', 'stats'] as const,
  scan: ['library', 'scan'] as const,
  continue: ['library', 'continue'] as const,
  recent: (kind?: LibraryKind) => ['library', 'recent', kind ?? 'all'] as const,
  genres: (kind?: LibraryKind) => ['library', 'genres', kind ?? 'all'] as const,
  items: (query: LibraryItemsQuery) => ['library', 'items', query] as const,
  item: (id: string) => ['library', 'item', id] as const,
  files: (status: string, search: string) => ['library', 'files', status, search] as const,
  candidates: (q: string, kind: string, year: string) => ['library', 'candidates', q, kind, year] as const
};

export const ITEMS_PAGE_SIZE = 60;

export function useLibraryStats() {
  return useQuery({ queryKey: libraryKeys.stats, queryFn: ({ signal }) => api.library.stats(signal), staleTime: 60_000 });
}

export function useContinueWatching() {
  return useQuery({ queryKey: libraryKeys.continue, queryFn: ({ signal }) => api.library.continue({ limit: 30 }, signal) });
}

export function useRecent(kind?: LibraryKind, enabled = true) {
  return useQuery({
    queryKey: libraryKeys.recent(kind),
    queryFn: ({ signal }) => api.library.recent({ kind, limit: 30 }, signal),
    enabled
  });
}

export function useGenres(kind?: LibraryKind, enabled = true) {
  return useQuery({
    queryKey: libraryKeys.genres(kind),
    queryFn: ({ signal }) => api.library.genres(kind, signal),
    staleTime: 5 * 60_000,
    enabled
  });
}

/** A single page of items (home rows). */
export function useItemsRow(query: LibraryItemsQuery, enabled = true) {
  return useQuery({
    queryKey: libraryKeys.items({ ...query, page: 1 }),
    queryFn: ({ signal }) => api.library.items({ pageSize: 24, ...query, page: 1 }, signal),
    enabled,
    staleTime: 2 * 60_000
  });
}

/** Every page of items (browse grid, infinite scroll). */
export function useItemsInfinite(query: Omit<LibraryItemsQuery, 'page'>) {
  return useInfiniteQuery({
    queryKey: [...libraryKeys.items(query), 'infinite'],
    queryFn: ({ pageParam, signal }) => api.library.items({ pageSize: ITEMS_PAGE_SIZE, ...query, page: pageParam }, signal),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page.hasNext ? last.page.page + 1 : undefined)
  });
}

export function useItem(id: string) {
  return useQuery({ queryKey: libraryKeys.item(id), queryFn: ({ signal }) => api.library.item(id, signal), enabled: !!id });
}

export function useReviewFiles(status: LibraryFileStatus, search: string) {
  return useInfiniteQuery({
    queryKey: libraryKeys.files(status, search),
    queryFn: ({ pageParam, signal }) =>
      api.library.files({ status, search: search || undefined, page: pageParam, pageSize: 50 }, signal),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page.hasNext ? last.page.page + 1 : undefined)
  });
}

/**
 * State of the library scan: the REST state, kept fresh by the `LibraryScan`
 * event of /hubs/events. When a scan ends the catalogue is refetched.
 */
export function useScanState() {
  const qc = useQueryClient();
  const query = useQuery({ queryKey: libraryKeys.scan, queryFn: ({ signal }) => api.library.scanState(signal), staleTime: 30_000 });
  useEventsMessage('LibraryScan', (e) => {
    const previous = qc.getQueryData<ApiLibraryScanStateDto | null>(libraryKeys.scan);
    const next: ApiLibraryScanStateDto | null = e.progress ?? (previous ? { ...previous, running: e.running } : null);
    qc.setQueryData(libraryKeys.scan, next);
    if (previous?.running && !e.running) void qc.invalidateQueries({ queryKey: libraryKeys.all, predicate: (q) => q.queryKey[1] !== 'scan' });
  });
  return query;
}

export function useScanActions() {
  const qc = useQueryClient();
  const start = useMutation({
    mutationFn: (force: boolean) => api.library.scan({ force }),
    onSuccess: () => {
      toast.success(t('Library scan started'));
      void qc.invalidateQueries({ queryKey: libraryKeys.scan });
    },
    onError: (error) => toast.error(t('Could not start the scan'), { description: errorMessage(error) })
  });
  const cancel = useMutation({
    mutationFn: () => api.library.cancelScan(),
    onSuccess: () => void qc.invalidateQueries({ queryKey: libraryKeys.scan }),
    onError: (error) => toast.error(t('Could not cancel the scan'), { description: errorMessage(error) })
  });
  return { start, cancel };
}

/** Invalidates everything the library shows (after a watched mark, a match...). */
export function useInvalidateLibrary() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: libraryKeys.all, predicate: (q) => q.queryKey[1] !== 'scan' && q.queryKey[1] !== 'candidates' });
}
