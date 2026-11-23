/**
 * Public API of the media feature, used by other features (file manager,
 * search, library, playlists, chat) to play or view media without depending
 * on the player's internals.
 *
 * CONTRACT — the media feature replaces the bodies, the signatures stay.
 * Heavy code must stay lazy: implementations should `import()` the player or
 * viewers on first use, because this module is imported from many places.
 */

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
  /** Media library progress reporting (movies/series). */
  watch?: { channelId: string; fileId: string; startAtSeconds?: number };
}

/** Replace the queue with `tracks` and start at `startIndex`. */
export function playAudio(tracks: AudioTrack[], startIndex = 0): void {
  console.warn('[player] playAudio not implemented yet', tracks.length, startIndex);
}

/** Add tracks after the current one ("play next") or at the end. */
export function enqueueAudio(tracks: AudioTrack[], position: 'next' | 'end' = 'end'): void {
  console.warn('[player] enqueueAudio not implemented yet', tracks.length, position);
}

/** Open the viewer on `items[index]` (images become a gallery, video a player...). */
export function openViewer(items: ViewerItem[], index = 0): void {
  console.warn('[player] openViewer not implemented yet', items.length, index);
}
