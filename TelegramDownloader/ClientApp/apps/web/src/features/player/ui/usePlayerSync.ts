import { useMutation, useQuery } from '@tanstack/react-query';
import { toast } from '@tfm/ui';
import { useEffect, useRef } from 'react';
import { t } from '@/i18n';
import { api } from '@/lib/api';
import { queryClient } from '@/lib/queryClient';
import { queryKeys } from '@/lib/queryKeys';
import { useEventsMessage } from '@/lib/realtime';
import { currentTrack } from '../queue';
import { usePlayer } from '../store';
import type { QueuedTrack } from '../queue';
import { PlayTracker, playerKey, toPlayerTrack, type TrackTags } from '../sync';

/** Optional features of the server (an old server answers 404: none). Fetched once per session. */
function useServerFeatures() {
  return useQuery({
    queryKey: queryKeys.capabilities,
    queryFn: async ({ signal }) => {
      try {
        return (await api.system.capabilities(signal))?.features ?? [];
      } catch {
        return [] as string[];
      }
    },
    staleTime: Infinity
  });
}

/** True when the server keeps favourites and history for every device (`player.sync`). */
export function usePlayerSyncEnabled(): boolean {
  return useServerFeatures().data?.includes('player.sync') ?? false;
}

export const playerSyncKeys = {
  favoriteKeys: ['player', 'favorites', 'keys'] as const
};

function useFavoriteKeys(enabled: boolean) {
  return useQuery({
    queryKey: playerSyncKeys.favoriteKeys,
    queryFn: async ({ signal }) => new Set(await api.player.favoriteKeys(signal)),
    enabled
  });
}

/** Favourite state of a track and a toggle (optimistic; rolled back on error). */
export function useFavorite(track: QueuedTrack | null, tags: TrackTags) {
  const enabled = usePlayerSyncEnabled();
  const key = track ? playerKey(track) : null;
  const keys = useFavoriteKeys(enabled);
  const isFavorite = !!key && !!keys.data?.has(key);

  const mutation = useMutation({
    mutationFn: async (add: boolean) => {
      if (!track || !key) return;
      if (add) {
        const body = toPlayerTrack(track, tags, usePlayer.getState().duration);
        if (body) await api.player.addFavorite(body);
      } else {
        await api.player.removeFavorite(key);
      }
    },
    onMutate: (add) => {
      const previous = queryClient.getQueryData<Set<string>>(playerSyncKeys.favoriteKeys);
      if (key) {
        const next = new Set(previous ?? []);
        if (add) next.add(key);
        else next.delete(key);
        queryClient.setQueryData(playerSyncKeys.favoriteKeys, next);
      }
      return { previous };
    },
    onError: (_error, _add, context) => {
      queryClient.setQueryData(playerSyncKeys.favoriteKeys, context?.previous);
      toast.error(t('Could not update the favourites'));
    },
    onSettled: () => void queryClient.invalidateQueries({ queryKey: playerSyncKeys.favoriteKeys })
  });

  return {
    /** The server supports it and the track can be a favourite (channel or local file). */
    available: enabled && !!key,
    isFavorite,
    toggle: () => mutation.mutate(!isFavorite)
  };
}

/**
 * Mounted once (by the player bar): records each play in the server history
 * after 30 s listened or at the end, and refreshes the favourites when another
 * device changes them.
 */
export function usePlayerSync(tags: TrackTags & { qid?: string }) {
  const enabled = usePlayerSyncEnabled();
  const enabledRef = useRef(enabled);
  const tagsRef = useRef(tags);
  useEffect(() => {
    enabledRef.current = enabled;
    tagsRef.current = tags;
  });

  useEffect(() => {
    const tracker = new PlayTracker();
    return usePlayer.subscribe((s) => {
      const track = currentTrack(s);
      if (!tracker.update(track?.qid ?? null, s.position, s.duration, s.status === 'playing')) return;
      if (!enabledRef.current || !track) return;
      const tags = tagsRef.current.qid === track.qid ? tagsRef.current : {};
      const body = toPlayerTrack(track, tags, s.duration);
      if (!body) return;
      void api.player
        .addHistory({ track: body, positionSeconds: s.position, completed: PlayTracker.completed(s.position, s.duration) })
        .catch(() => undefined); // the history is best effort
    });
  }, []);

  useEventsMessage('PlayerDataChanged', (e) => {
    if (e.kind === 'favorites') void queryClient.invalidateQueries({ queryKey: playerSyncKeys.favoriteKeys });
  });
}
