import type { ApiChatMessageDto } from '@tfm/api-client';
import { Button, Checkbox, IconButton } from '@tfm/ui';
import { CloudDownload, Download, X } from 'lucide-react';
import { useState } from 'react';
import { t } from '@/i18n';
import { formatBytes } from '@/lib/format';
import { saveMessages } from '../chatActions';
import type { MessageSelection } from '../useSelection';
import { DownloadMessagesDialog } from './DownloadMessagesDialog';

export interface SelectionBarProps {
  channelId: string;
  selection: MessageSelection;
  /** Loaded messages that can be chosen. */
  available: ApiChatMessageDto[];
  /** Shown with nothing chosen. */
  hint: string;
  /** Leaves the selection. */
  onClose?: () => void;
}

/** Chosen files, and what to do with them. */
export function SelectionBar({ channelId, selection, available, hint, onClose }: SelectionBarProps) {
  const [downloading, setDownloading] = useState(false);
  const count = selection.ids.length;
  const size = selection.messages.reduce((sum, m) => sum + m.fileSize, 0);
  const all = available.length > 0 && available.every((m) => selection.has(m.id));
  const done = () => {
    selection.clear();
    onClose?.();
  };
  return (
    <div className="glass sticky top-0 z-10 flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-border px-4 py-2 sm:px-6">
      <Checkbox
        checked={all ? true : count > 0 ? 'indeterminate' : false}
        onCheckedChange={(v) => (v === true ? selection.set(available) : selection.clear())}
        aria-label={t('Select all')}
        title={t('Select all')}
      />
      <span className="min-w-44 flex-1 truncate text-sm text-fg-muted">
        {count > 0 ? [t('{count} selected', { count }), size > 0 ? formatBytes(size) : ''].filter(Boolean).join(' · ') : hint}
      </span>
      {count > 0 && (
        <>
          <IconButton size="sm" label={t('Download to this device')} onClick={() => saveMessages(channelId, selection.messages)}>
            <Download />
          </IconButton>
          <Button size="sm" className="max-sm:order-last max-sm:w-full" icon={<CloudDownload />} onClick={() => setDownloading(true)}>
            {t('Download to server')}
          </Button>
        </>
      )}
      {(onClose || count > 0) && (
        <IconButton size="sm" label={t('Cancel the selection')} onClick={done}>
          <X />
        </IconButton>
      )}
      <DownloadMessagesDialog channelId={channelId} ids={downloading ? selection.ids : null} onClose={() => setDownloading(false)} onQueued={done} />
    </div>
  );
}
