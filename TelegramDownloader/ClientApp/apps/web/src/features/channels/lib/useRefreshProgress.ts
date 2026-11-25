import type { ApiChannelRefreshProgressDto, ApiChannelRefreshResultDto } from '@tfm/api-client';
import { fileManagerQueryKey } from '@tfm/file-manager';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { useEventsMessage } from '@/lib/realtime';
import { channelProviderId } from './channelProvider';

export const refreshKeys = {
  progress: (channelId: string) => ['channels', 'refresh-progress', channelId] as const,
  result: (channelId: string) => ['channels', 'refresh-result', channelId] as const
};

/**
 * Live refresh state of a channel: the progress endpoint first (and as a slow
 * poll while running, in case the hub is down), then `ChannelRefreshProgress`
 * messages of /hubs/events. `ChannelRefreshed` stores the result and reloads
 * the listing and the channel statistics.
 */
export function useRefreshProgress(channelId: string) {
  const qc = useQueryClient();
  const progress = useQuery({
    queryKey: refreshKeys.progress(channelId),
    queryFn: ({ signal }) => api.channels.refreshProgress(channelId, signal),
    refetchInterval: (q) => (q.state.data ? 4000 : false),
    staleTime: 2000
  });
  const result = useQuery<ApiChannelRefreshResultDto | null>({
    queryKey: refreshKeys.result(channelId),
    queryFn: ({ signal }) => api.channels.refreshResult(channelId, signal),
    staleTime: Infinity
  });

  useEventsMessage('ChannelRefreshProgress', (e) => {
    if (String(e.channelId) !== String(channelId)) return;
    qc.setQueryData<ApiChannelRefreshProgressDto | null>(refreshKeys.progress(channelId), e.progress);
  });
  useEventsMessage('ChannelRefreshed', (e) => {
    if (String(e.channelId) !== String(channelId)) return;
    qc.setQueryData(refreshKeys.progress(channelId), null);
    qc.setQueryData(refreshKeys.result(channelId), e.result);
    void qc.invalidateQueries({ queryKey: fileManagerQueryKey(channelProviderId(channelId)) });
    void qc.invalidateQueries({ queryKey: queryKeys.channel(channelId) });
    void qc.invalidateQueries({ queryKey: queryKeys.channels });
  });

  const running = progress.data && progress.data.phase !== 'done' ? progress.data : null;
  return { progress: running, result: result.data ?? null, refetch: progress.refetch };
}
