import { useMemo } from 'react';
import { t } from '@/i18n';
import { useChannels } from '@/shell/useChannels';

export interface ChannelOption {
  id: number;
  name: string;
}

/** Name of a channel id, or "Channel 123" when it is not in the list. */
export function channelName(options: ChannelOption[], id: number): string {
  return options.find((c) => c.id === id)?.name ?? t('Channel {id}', { id });
}

/** Channels of the account as picker options (`indexedOnly`: only those with a local index). */
export function useChannelOptions(indexedOnly = false) {
  const channels = useChannels();
  const options = useMemo<ChannelOption[]>(
    () => (channels.data ?? []).filter((c) => !indexedOnly || c.hasDatabase).map((c) => ({ id: c.id, name: c.name })),
    [channels.data, indexedOnly]
  );
  return { options, isPending: channels.isPending, data: channels.data };
}
