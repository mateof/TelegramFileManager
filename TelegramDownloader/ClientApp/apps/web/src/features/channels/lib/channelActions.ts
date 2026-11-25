import type { FileAction, FileEntry } from '@tfm/file-manager';
import { toast } from '@tfm/ui';
import { Clapperboard, HardDriveDownload, MessageSquare, Send, Share2, Tv, Zap } from 'lucide-react';
import { createElement } from 'react';
import { dtoOf, toApiFolder } from '@/features/files/entries';
import { mediaActions } from '@/features/files/media';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';

/** Dialogs the channel actions open (rendered by the page). */
export type ChannelDialog =
  | { type: 'download'; entries: FileEntry[] }
  | { type: 'strm'; path: string; name: string }
  | { type: 'series'; path: string; name: string }
  | { type: 'share'; folderId?: string; name: string };

export interface ChannelActionsOptions {
  channelId: string;
  channelName: string;
  /** Imported shared collection (read only: no preload/STRM/share/Telegram link). */
  collectionId?: string;
  openDialog(dialog: ChannelDialog): void;
  /** Shows a message of the channel in its messages page. */
  openMessage?(messageId: number): void;
}

/** The message of a file, or of its first part when it is split. */
export function messageIdOf(entry: FileEntry): number | null {
  if (entry.kind !== 'file') return null;
  const dto = dtoOf(entry);
  return dto?.firstMessageId ?? dto?.messageId ?? null;
}

const icon = (c: Parameters<typeof createElement>[0]) => createElement(c);

/** "song.mp3" or "3 items". */
export function countLabel(entries: FileEntry[]): string {
  return entries.length === 1 ? entries[0].name : t('{count} items', { count: entries.length });
}

/** TFM actions of the channel file manager (on top of the built-in ones). */
export function channelActions({ channelId, channelName, collectionId, openDialog, openMessage }: ChannelActionsOptions): FileAction[] {
  const own = !collectionId;
  const actions: FileAction[] = [
    {
      id: 'tfm:download-server',
      label: t('Download to the server'),
      icon: icon(HardDriveDownload),
      group: 'a-open',
      order: 11,
      run: (entries) => openDialog({ type: 'download', entries })
    },
    ...mediaActions({ channelId, sourceName: channelName })
  ];
  if (!own) return actions;

  actions.push(
    {
      id: 'tfm:preload',
      label: t('Preload into the cache'),
      icon: icon(Zap),
      group: 'b-media',
      order: 5,
      run: async (entries) => {
        try {
          const res = await api.files.preload(channelId, { fileIds: entries.map((e) => e.id) });
          toast.success(t('Preloading {count} items', { count: res?.accepted ?? entries.length }), {
            description: t('You will get a notification when it finishes.')
          });
        } catch (error) {
          toast.error(t('Could not preload'), { description: errorMessage(error) });
        }
      }
    },
    {
      id: 'tfm:strm',
      label: t('Export STRM files'),
      icon: icon(Clapperboard),
      selection: 'single',
      group: 'c-share',
      order: 1,
      placement: ['context', 'details'],
      when: ([e]) => e.kind === 'folder',
      run: ([e]) => openDialog({ type: 'strm', path: e.path, name: e.name })
    },
    {
      id: 'tfm:strm-current',
      label: t('Export STRM files of this folder'),
      icon: icon(Clapperboard),
      selection: 'none',
      group: 'c-share',
      order: 1,
      placement: ['context'],
      run: (_e, ctx) => openDialog({ type: 'strm', path: ctx.currentPath, name: ctx.currentPath === '/' ? channelName : ctx.currentPath.split('/').pop() ?? '' })
    },
    {
      id: 'tfm:series',
      label: t('Identify as a series'),
      icon: icon(Tv),
      selection: 'single',
      group: 'b-media',
      order: 8,
      placement: ['context', 'details'],
      when: ([e]) => e.kind === 'folder',
      run: ([e]) => openDialog({ type: 'series', path: toApiFolder(e.path), name: e.name })
    },
    {
      id: 'tfm:series-current',
      label: t('Identify this folder as a series'),
      icon: icon(Tv),
      selection: 'none',
      group: 'b-media',
      order: 8,
      placement: ['context'],
      run: (_e, ctx) => openDialog({ type: 'series', path: toApiFolder(ctx.currentPath), name: ctx.currentPath === '/' ? channelName : ctx.currentPath.split('/').pop() ?? '' })
    },
    {
      id: 'tfm:share',
      label: t('Share'),
      icon: icon(Share2),
      selection: 'single',
      group: 'c-share',
      order: 2,
      placement: ['context', 'details', 'selection'],
      when: ([e]) => e.kind === 'folder',
      run: ([e]) => openDialog({ type: 'share', folderId: e.id, name: e.name })
    },
    {
      id: 'tfm:share-channel',
      label: t('Share this channel'),
      icon: icon(Share2),
      selection: 'none',
      group: 'c-share',
      order: 2,
      placement: ['context'],
      run: () => openDialog({ type: 'share', name: channelName })
    },
    {
      id: 'tfm:go-to-message',
      label: t('Go to the message'),
      icon: icon(MessageSquare),
      selection: 'single',
      group: 'a-open',
      order: 12,
      placement: ['context', 'details', 'selection'],
      when: ([e]) => !!openMessage && messageIdOf(e) !== null,
      run: ([e]) => {
        const messageId = messageIdOf(e);
        if (messageId !== null) openMessage?.(messageId);
      }
    },
    {
      id: 'tfm:open-telegram',
      label: t('Open in Telegram'),
      icon: icon(Send),
      selection: 'single',
      group: 'c-share',
      order: 5,
      placement: ['context', 'details'],
      when: ([e]) => messageIdOf(e) !== null,
      run: ([e]) => {
        const id = String(channelId).replace(/^-100/, '');
        window.location.href = `tg://privatepost?channel=${id}&post=${messageIdOf(e)}`;
      }
    }
  );
  return actions;
}
