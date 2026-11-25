import type { ApiChannelDto } from '@tfm/api-client';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from '@tfm/ui';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';

const PAGE_SIZE = 500;

/** Every channel visible to the account (all pages of `GET /api/v1/channels`). */
export function useChannels() {
  return useQuery({
    queryKey: queryKeys.channels,
    queryFn: async ({ signal }) => {
      const all: ApiChannelDto[] = [];
      for (let page = 1; ; page++) {
        const res = await api.channels.list({ page, pageSize: PAGE_SIZE, sortBy: 'name' }, signal);
        all.push(...res.items);
        if (!res.page.hasNext || res.items.length === 0) break;
      }
      return all;
    },
    staleTime: 5 * 60_000
  });
}

/** Telegram folders with their channels. Only fetched when `enabled`. */
export function useChannelFolders(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.channelFolders,
    queryFn: ({ signal }) => api.channels.folders(signal),
    enabled,
    staleTime: 5 * 60_000
  });
}

/** Optimistic favourite toggle. */
export function useToggleFavourite() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, favourite }: { id: number; favourite: boolean }) =>
      favourite ? api.channels.addFavorite(id) : api.channels.removeFavorite(id),
    onMutate: async ({ id, favourite }) => {
      await qc.cancelQueries({ queryKey: queryKeys.channels });
      const previous = qc.getQueryData<ApiChannelDto[]>(queryKeys.channels);
      qc.setQueryData<ApiChannelDto[]>(queryKeys.channels, (list) =>
        list?.map((c) => (c.id === id ? { ...c, isFavorite: favourite } : c))
      );
      return { previous };
    },
    onError: (error, _vars, ctx) => {
      if (ctx?.previous) qc.setQueryData(queryKeys.channels, ctx.previous);
      toast.error(t('Could not update the favourites'), { description: errorMessage(error) });
    },
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: queryKeys.channelFolders });
    }
  });
}
