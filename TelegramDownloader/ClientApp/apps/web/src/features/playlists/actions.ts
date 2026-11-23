import type { PlaylistModel } from '@tfm/api-client';
import { confirm, toast } from '@tfm/ui';
import { enqueueAudio, playAudio } from '@/features/player/public';
import { usePlayer } from '@/features/player/store';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { playlistTrackToAudio } from './convert';
import { invalidatePlaylists } from './queries';

/** Play a whole playlist (optionally shuffled), from `start`. */
export function playPlaylist(p: PlaylistModel, options: { shuffle?: boolean; start?: number } = {}): void {
  if (p.tracks.length === 0) {
    toast(t('This playlist is empty'));
    return;
  }
  usePlayer.setState({ shuffle: !!options.shuffle });
  const start = options.shuffle && options.start === undefined ? Math.floor(Math.random() * p.tracks.length) : (options.start ?? 0);
  playAudio(p.tracks.map(playlistTrackToAudio), start);
}

export function queuePlaylist(p: PlaylistModel, position: 'next' | 'end'): void {
  if (p.tracks.length) enqueueAudio(p.tracks.map(playlistTrackToAudio), position);
}

export async function deletePlaylist(p: PlaylistModel): Promise<boolean> {
  const ok = await confirm({
    title: t('Delete "{name}"?', { name: p.name }),
    description: t('The playlist is deleted; the files stay where they are.'),
    confirmLabel: t('Delete'),
    destructive: true
  });
  if (!ok) return false;
  try {
    await api.playlists.delete(p.id);
    toast.success(t('Playlist deleted'));
    void invalidatePlaylists();
    return true;
  } catch (e) {
    toast.error(t('Could not delete the playlist'), { description: errorMessage(e) });
    return false;
  }
}

