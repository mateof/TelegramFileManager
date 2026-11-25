/**
 * Public API of the media feature, used by other features (file manager,
 * search, library, playlists, chat) to play or view media without depending
 * on the player's internals.
 *
 * This module is imported from many places, so it stays tiny: the playback
 * engine and the viewers are loaded with `import()` on first use.
 */
import { useViewer } from './viewerStore';

/** A playable audio item. URLs must be playable by an <audio> element (same-origin cookie auth). */
export interface AudioTrack {
  /** Stable id (file id, or local path for local files). */
  id: string;
  title: string;
  artist?: string;
  album?: string;
  /** Source label shown under the title (channel name, "Local files"...). */
  source?: string;
  url: string;
  coverUrl?: string;
  /** Channel id + file id when the track comes from a channel (playlists use them). */
  channelId?: string;
  fileId?: string;
  /** Local path when it comes from the server's local files. */
  localPath?: string;
  size?: number;
  durationSeconds?: number;
}

/** Something to show in the viewer (image gallery, video player, PDF, text). */
export interface ViewerItem {
  id: string;
  name: string;
  kind: 'image' | 'video' | 'audio' | 'pdf' | 'text' | 'other';
  /** Inline URL (Range-capable for video). */
  url: string;
  /** Optional transcoding URL for video formats the browser can't play natively. */
  playbackUrl?: string;
  downloadUrl?: string;
  thumbnailUrl?: string;
  size?: number;
  mimeType?: string;
  /** A button of the viewer that closes it and goes somewhere (the message of the file). */
  goTo?: { label: string; run: () => void };
  /** Media library progress reporting (movies/series). */
  watch?: { channelId: string; fileId: string; startAtSeconds?: number };
}

const loadEngine = () => import('./engine').then((m) => m.getEngine());

/** Replace the queue with `tracks` and start at `startIndex`. */
export function playAudio(tracks: AudioTrack[], startIndex = 0): void {
  if (tracks.length === 0) return;
  void loadEngine().then((engine) => engine.playQueue(tracks, startIndex));
}

/** Add tracks after the current one ("play next") or at the end. */
export function enqueueAudio(tracks: AudioTrack[], position: 'next' | 'end' = 'end'): void {
  if (tracks.length === 0) return;
  void loadEngine().then((engine) => engine.enqueue(tracks, position));
}

/** Open the viewer on `items[index]` (images become a gallery, video a player...). */
export function openViewer(items: ViewerItem[], index = 0): void {
  const item = items[index];
  if (!item) return;
  // Audio goes to the global player instead of an overlay
  if (item.kind === 'audio') {
    const audio = items.filter((i) => i.kind === 'audio');
    playAudio(
      audio.map((i) => ({ id: i.id, title: i.name, url: i.playbackUrl ?? i.url, coverUrl: i.thumbnailUrl, size: i.size })),
      audio.indexOf(item)
    );
    return;
  }
  useViewer.getState().show(items, index);
}
