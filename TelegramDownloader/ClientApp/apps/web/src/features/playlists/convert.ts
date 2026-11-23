import type { PlaylistTrackModel, PlaylistTrackWriteRequest } from '@tfm/api-client';
import type { AudioTrack } from '@/features/player/public';
import { api } from '@/lib/api';
import type { PlaylistCandidate } from './public';

/**
 * Conversions between playlist tracks (server), player tracks and the
 * candidates other features pass to `openAddToPlaylist`.
 */

const enc = encodeURIComponent;

export function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(dot).toLowerCase() : '';
}

/** Inline bytes of a channel file (v1, Range-capable, protected by the web session). */
export function channelContentUrl(channelId: string, fileId: string): string {
  return api.http.url(`/api/v1/channels/${enc(channelId)}/files/${enc(fileId)}/content`);
}

/** A playlist track as a player track. */
export function playlistTrackToAudio(track: PlaylistTrackModel): AudioTrack {
  const local = track.isLocalFile || !!track.directUrl;
  return {
    id: local ? (track.directUrl ?? track.fileId) : `${track.channelId}:${track.fileId}`,
    title: track.fileName,
    source: local ? undefined : track.channelName || undefined,
    url: local && track.directUrl ? track.directUrl : channelContentUrl(track.channelId, track.fileId),
    channelId: local ? undefined : track.channelId,
    fileId: track.fileId,
    localPath: local ? track.filePath || undefined : undefined,
    size: track.fileSize || undefined
  };
}

/** Body to add a candidate to a playlist. */
export function candidateToWrite(c: PlaylistCandidate): PlaylistTrackWriteRequest {
  const local = c.isLocalFile || c.channelId === 'local';
  return {
    fileId: c.fileId,
    channelId: local ? 'local' : c.channelId,
    fileName: c.fileName,
    filePath: c.filePath,
    fileType: extensionOf(c.fileName),
    fileSize: c.fileSize ?? 0,
    directUrl: local && c.filePath ? api.local.downloadUrl(c.filePath) : null
  };
}

/** A player track as a playlist candidate, or null when it cannot be stored (no channel file, no local path). */
export function audioToCandidate(track: AudioTrack): PlaylistCandidate | null {
  if (track.channelId && track.fileId) {
    return { channelId: track.channelId, fileId: track.fileId, fileName: track.title, fileSize: track.size };
  }
  if (track.localPath) {
    return { channelId: 'local', fileId: track.fileId ?? track.localPath, fileName: track.title, fileSize: track.size, filePath: track.localPath, isLocalFile: true };
  }
  return null;
}

/** Key used to detect a candidate already in a playlist. */
export function trackKey(t: { fileId?: string | null; directUrl?: string | null }): string {
  return t.fileId || t.directUrl || '';
}
