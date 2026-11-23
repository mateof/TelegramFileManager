/**
 * Public API of the playlists feature for other features (file manager,
 * search, player...).
 *
 * CONTRACT — the media/playlists feature replaces the body, the signature stays.
 * Implementations should lazy-load the picker dialog.
 */

/** An item that can be added to a server playlist (`POST /api/v1/playlists/{id}/tracks`). */
export interface PlaylistCandidate {
  channelId: string;
  fileId: string;
  fileName: string;
  fileSize?: number;
  /** Local files: their path (channelId is 'local'). */
  filePath?: string;
  isLocalFile?: boolean;
}

/** Open the "add to playlist" picker (choose or create a playlist) for these items. */
export function openAddToPlaylist(items: PlaylistCandidate[]): void {
  console.warn('[playlists] openAddToPlaylist not implemented yet', items.length);
}
