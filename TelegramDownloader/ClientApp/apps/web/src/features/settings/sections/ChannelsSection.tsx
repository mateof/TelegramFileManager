import { Avatar, Badge, Button, confirm, Skeleton, toast } from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Eye, EyeOff, Image, RefreshCw, Trash2 } from 'lucide-react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { SettingRow, SettingsGroup, SwitchRow } from '../components/SettingRow';
import { settingsKeys } from '../model/useSettingsForm';
import type { FormSectionProps } from './types';

function ImageCacheRow() {
  const qc = useQueryClient();
  const cache = useQuery({ queryKey: settingsKeys.imageCache, queryFn: ({ signal }) => api.system.imageCache(signal) });
  const clear = useMutation({
    mutationFn: () => api.system.clearImageCache(),
    onSuccess: (info) => {
      qc.setQueryData(settingsKeys.imageCache, info);
      toast.success(t('Image cache cleared'));
    },
    onError: (error) => toast.error(t('Could not clear the image cache'), { description: errorMessage(error) })
  });
  const total = (cache.data?.channelImages ?? 0) + (cache.data?.thumbnails ?? 0) + (cache.data?.libraryImages ?? 0);
  return (
    <SettingRow
      id="imageCache"
      extra={
        cache.data && (
          <p className="mt-1.5 text-xs text-fg-subtle">
            {t('{channels} channel images · {thumbs} thumbnails · {covers} library images · {size}', { channels: cache.data.channelImages, thumbs: cache.data.thumbnails, covers: cache.data.libraryImages ?? 0, size: cache.data.sizeText })}
          </p>
        )
      }
    >
      <Button
        variant="outline"
        size="sm"
        icon={<Trash2 />}
        loading={clear.isPending}
        disabled={total === 0}
        onClick={async () => {
          if (await confirm({ title: t('Clear the image cache?'), description: t('Images are downloaded again when they are shown.'), confirmLabel: t('Clear') })) clear.mutate();
        }}
      >
        {t('Clear cache')}
      </Button>
    </SettingRow>
  );
}

function HiddenChannels() {
  const qc = useQueryClient();
  const hidden = useQuery({ queryKey: settingsKeys.hidden, queryFn: ({ signal }) => api.channels.hidden(signal) });
  const unhide = useMutation({
    mutationFn: (id: number) => api.channels.unhide(id),
    onMutate: async (id) => {
      await qc.cancelQueries({ queryKey: settingsKeys.hidden });
      const previous = qc.getQueryData<typeof hidden.data>(settingsKeys.hidden);
      qc.setQueryData<typeof hidden.data>(settingsKeys.hidden, (list) => list?.filter((c) => c.id !== id));
      return { previous };
    },
    onError: (error, _id, ctx) => {
      if (ctx?.previous) qc.setQueryData(settingsKeys.hidden, ctx.previous);
      toast.error(t('Could not unhide the channel'), { description: errorMessage(error) });
    },
    onSuccess: () => toast.success(t('Channel visible again')),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: queryKeys.channels });
      void qc.invalidateQueries({ queryKey: queryKeys.channelFolders });
      void qc.invalidateQueries({ queryKey: settingsKeys.config });
    }
  });

  return (
    <SettingRow id="hiddenChannels" stacked badge={hidden.data && <Badge>{hidden.data.length}</Badge>}>
      {hidden.isPending ? (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-10" />
          <Skeleton className="h-10" />
        </div>
      ) : hidden.isError ? (
        <div className="flex items-center gap-2 text-sm text-danger">
          {errorMessage(hidden.error)}
          <Button size="xs" variant="ghost" icon={<RefreshCw />} onClick={() => void hidden.refetch()}>
            {t('Retry')}
          </Button>
        </div>
      ) : hidden.data.length === 0 ? (
        <p className="flex items-center gap-2 text-sm text-fg-muted">
          <Eye className="size-4" /> {t('No hidden channels.')}
        </p>
      ) : (
        <ul className="flex flex-col divide-y divide-border rounded-xl border border-border">
          {hidden.data.map((c) => (
            <li key={c.id} className="flex items-center gap-3 px-3 py-2">
              <Avatar src={c.imageUrl} name={c.name} size="sm" />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm text-fg">{c.name}</p>
                <p className="text-[11px] text-fg-subtle">{c.id}</p>
              </div>
              <Button size="xs" variant="soft" icon={<Eye />} onClick={() => unhide.mutate(c.id)}>
                {t('Unhide')}
              </Button>
            </li>
          ))}
        </ul>
      )}
    </SettingRow>
  );
}

export function ChannelsSection({ form, values: v }: FormSectionProps) {
  return (
    <>
      <SettingsGroup title={t('Data refresh')} icon={<RefreshCw />}>
        <SwitchRow id="enableRefreshOwnChannels" checked={v.enableRefreshOwnChannels} onChange={(b) => form.setField('enableRefreshOwnChannels', b)} />
      </SettingsGroup>
      <SettingsGroup title={t('Channel images')} icon={<Image />}>
        <SwitchRow id="showChannelImages" checked={v.showChannelImages} onChange={(b) => form.setField('showChannelImages', b)} />
        <ImageCacheRow />
      </SettingsGroup>
      <SettingsGroup title={t('Hidden channels')} icon={<EyeOff />}>
        <SwitchRow id="showHiddenChannels" checked={v.showHiddenChannels} onChange={(b) => form.setField('showHiddenChannels', b)} />
        <HiddenChannels />
      </SettingsGroup>
    </>
  );
}
