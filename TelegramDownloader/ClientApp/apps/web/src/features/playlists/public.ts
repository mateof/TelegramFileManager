/**
 * Public API of the playlists feature for other features (file manager,
 * search, player...). Tiny on purpose: the picker dialog is lazy
 * (rendered by PlaylistPickerSlot, which the player slot mounts).
 */
import { usePlaylistPicker } from './pickerStore';

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
  if (items.length === 0) return;
  usePlaylistPicker.setState({ items, open: true });
}
