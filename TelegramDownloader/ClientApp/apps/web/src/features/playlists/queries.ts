import type { PlaylistModel, PlaylistSortBy } from '@tfm/api-client';
import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { queryClient } from '@/lib/queryClient';

/** Query keys of the playlists feature. */
export const playlistKeys = {
  all: ['playlists'] as const,
  list: (sort: PlaylistSortBy) => ['playlists', 'list', sort] as const,
  detail: (id: string) => ['playlists', 'detail', id] as const
};

/** Every playlist (the API caps a page at 500, more than anyone keeps). */
export function usePlaylists(sort: PlaylistSortBy = 'name') {
  return useQuery({
    queryKey: playlistKeys.list(sort),
    queryFn: async ({ signal }) => (await api.playlists.list({ pageSize: 500, sortBy: sort, sortDescending: sort !== 'name' }, signal)).items
  });
}

export function usePlaylist(id: string) {
  return useQuery({ queryKey: playlistKeys.detail(id), queryFn: ({ signal }) => api.playlists.get(id, signal) });
}

/** Put a fresh playlist (from a mutation answer) in the cache and refresh the lists. */
export function storePlaylist(p: PlaylistModel): void {
  queryClient.setQueryData(playlistKeys.detail(p.id), p);
  void queryClient.invalidateQueries({ queryKey: ['playlists', 'list'] });
}

export function invalidatePlaylists(): Promise<void> {
  return queryClient.invalidateQueries({ queryKey: playlistKeys.all });
}
