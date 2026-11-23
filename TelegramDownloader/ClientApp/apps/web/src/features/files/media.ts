import { createElement } from 'react';
import { entryMediaKind, type FileAction, type FileEntry } from '@tfm/file-manager';
import { toast } from '@tfm/ui';
import { Link2, ListEnd, ListPlus, ListVideo, Play } from 'lucide-react';
import { enqueueAudio, openViewer, playAudio } from '@/features/player/public';
import { openAddToPlaylist, type PlaylistCandidate } from '@/features/playlists/public';
import { t } from '@/i18n';
import { absoluteUrl, copyText } from './browser';
import { dtoOf, toTrack, toViewerItem, viewerKind } from './entries';

export interface MediaSource {
  /** Channel holding the files (undefined for local files). */
  channelId?: string;
  /** Label shown by the player under the title. */
  sourceName?: string;
  local?: boolean;
  /** Inline URL override (local files). */
  inlineUrl?(entry: FileEntry): string | undefined;
  downloadUrl?(entry: FileEntry): string | undefined;
}

const isAudio = (e: FileEntry) => e.kind === 'file' && entryMediaKind(e) === 'audio';

/**
 * Opens a file with the app's player/viewer: audio plays with the folder's
 * audio files as the queue; images open as a gallery of the folder; video,
 * PDF and text in the viewer. Returns false for other files.
 */
export function openMedia(entry: FileEntry, folderEntries: FileEntry[], source: MediaSource): boolean {
  const kind = viewerKind(entry);
  if (kind === 'audio') {
    const audios = folderEntries.filter(isAudio);
    const list = audios.some((a) => a.id === entry.id) ? audios : [entry];
    playAudio(
      list.map((e) => toTrack(e, source)),
      Math.max(0, list.findIndex((a) => a.id === entry.id))
    );
    return true;
  }
  if (!kind) return false;
  const same = kind === 'image' ? folderEntries.filter((e) => e.kind === 'file' && viewerKind(e) === 'image') : [entry];
  const list = same.some((e) => e.id === entry.id) ? same : [entry];
  openViewer(
    list.map((e) => toViewerItem(e, source.inlineUrl?.(e), source.downloadUrl?.(e))),
    Math.max(0, list.findIndex((e) => e.id === entry.id))
  );
  return true;
}

function playlistCandidates(entries: FileEntry[], source: MediaSource): PlaylistCandidate[] {
  return entries.filter(isAudio).map((e) => {
    const dto = dtoOf(e);
    return source.local
      ? { channelId: 'local', fileId: dto.id, fileName: e.name, fileSize: dto.size, filePath: dto.id, isLocalFile: true }
      : { channelId: source.channelId ?? '', fileId: dto.id, fileName: e.name, fileSize: dto.size };
  });
}

/** Player / playlist / media URL actions shared by the channel, collection and local file managers. */
export function mediaActions(source: MediaSource): FileAction[] {
  const allAudio = (entries: FileEntry[]) => entries.length > 0 && entries.every(isAudio);
  return [
    {
      id: 'tfm:play',
      label: t('Play'),
      icon: createElement(Play),
      group: 'a-open',
      order: 1,
      placement: ['selection', 'details'],
      when: allAudio,
      run: (entries) => playAudio(entries.map((e) => toTrack(e, source)))
    },
    {
      id: 'tfm:play-next',
      label: t('Play next'),
      icon: createElement(ListVideo),
      group: 'b-media',
      order: 1,
      when: allAudio,
      run: (entries) => {
        enqueueAudio(
          entries.map((e) => toTrack(e, source)),
          'next'
        );
        toast.success(t('Added to the queue'));
      }
    },
    {
      id: 'tfm:enqueue',
      label: t('Add to queue'),
      icon: createElement(ListEnd),
      group: 'b-media',
      order: 2,
      when: allAudio,
      run: (entries) => {
        enqueueAudio(entries.map((e) => toTrack(e, source)));
        toast.success(t('Added to the queue'));
      }
    },
    {
      id: 'tfm:add-to-playlist',
      label: t('Add to playlist'),
      icon: createElement(ListPlus),
      group: 'b-media',
      order: 3,
      when: allAudio,
      run: (entries) => openAddToPlaylist(playlistCandidates(entries, source))
    },
    {
      id: 'tfm:copy-url',
      label: t('Copy media URL'),
      icon: createElement(Link2),
      selection: 'single',
      group: 'b-media',
      order: 10,
      placement: ['context', 'details'],
      when: ([e]) => e.kind === 'file' && !!(dtoOf(e).streamUrl ?? dtoOf(e).contentUrl),
      run: async ([e]) => {
        const dto = dtoOf(e);
        try {
          await copyText(absoluteUrl(dto.streamUrl ?? dto.contentUrl ?? ''));
          toast.success(t('Copied to the clipboard'));
        } catch {
          toast.error(t('Could not copy'));
        }
      }
    }
  ];
}
