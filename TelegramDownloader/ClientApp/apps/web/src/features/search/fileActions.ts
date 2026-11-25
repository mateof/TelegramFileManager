import type { ApiFileDto } from '@tfm/api-client';
import { toast } from '@tfm/ui';
import { enqueueAudio, openViewer, playAudio } from '@/features/player/public';
import { openAddToPlaylist } from '@/features/playlists/public';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { isLocal, mediaKind, toAudioTrack, toPlaylistCandidate, toViewerItem, type MediaFileDto } from './fileMedia';

/** Plays the audio files of `files`, starting at `start` (a file of the list). */
export function playFiles(files: ApiFileDto[], channelId: string | null, source: string, start?: ApiFileDto) {
  const audio = files.filter((f) => mediaKind(f) === 'audio');
  const tracks = audio.map((f) => toAudioTrack(f as MediaFileDto, channelId, source)).filter((x) => x !== null);
  if (!tracks.length) {
    toast.error(t('Nothing to play'));
    return;
  }
  const index = start ? Math.max(0, audio.findIndex((f) => f.id === start.id)) : 0;
  playAudio(tracks, index);
}

export function enqueueFiles(files: ApiFileDto[], channelId: string | null, source: string, position: 'next' | 'end') {
  const tracks = files
    .filter((f) => mediaKind(f) === 'audio')
    .map((f) => toAudioTrack(f as MediaFileDto, channelId, source))
    .filter((x) => x !== null);
  if (!tracks.length) return;
  enqueueAudio(tracks, position);
  toast.success(position === 'next' ? t('Playing next') : t('Added to the queue'));
}

/** Opens the viewer on `file`; images and videos of `siblings` become a gallery. */
export function viewFile(file: ApiFileDto, siblings: ApiFileDto[], channelId: string | null) {
  const kind = mediaKind(file);
  const pool = kind === 'image' || kind === 'video' ? siblings.filter((f) => mediaKind(f) === kind) : [file];
  const items = pool.map((f) => toViewerItem(f as MediaFileDto, channelId)).filter((x) => x !== null);
  const index = Math.max(0, pool.findIndex((f) => f.id === file.id));
  if (!items.length) {
    toast.error(t('This file cannot be previewed'));
    return;
  }
  openViewer(items, Math.min(index, items.length - 1));
}

export function addToPlaylist(files: ApiFileDto[], channelId: string | null) {
  openAddToPlaylist(files.map((f) => toPlaylistCandidate(f, channelId)));
}

/** Queues a server-side download of channel files into the local storage. */
export async function downloadToServer(files: ApiFileDto[], channelId: string | null) {
  if (isLocal(channelId) || !files.length) return;
  try {
    const res = await api.transfers.startDownload({ channelId: String(channelId), fileIds: files.map((f) => f.id) });
    toast.success(t('Download queued'), { description: t('{count} files sent to the server downloads', { count: res.accepted }) });
  } catch (error) {
    toast.error(t('Could not start the download'), { description: errorMessage(error) });
  }
}
