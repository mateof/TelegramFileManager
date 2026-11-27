import type { PlayerTrackInput } from '@tfm/api-client';
import type { AudioTrack } from './public';

/**
 * Pure helpers of the shared player data (`player.sync`: favourites and play
 * history kept on the server for every device). No DOM, no API: the hooks in
 * ui/usePlayerSync.ts use them and the tests exercise them directly.
 */

/** Same normalisation as the server: `/` separators, no surrounding blanks, no leading slash. */
export function normalizeLocalPath(path: string): string {
  return path.replace(/\\/g, '/').trim().replace(/^\/+/, '');
}

/** Server key of a track, or null when it is neither an indexed channel file nor a local file. */
export function playerKey(track: Pick<AudioTrack, 'channelId' | 'fileId' | 'localPath'>): string | null {
  if (track.channelId && track.fileId) return `channel:${track.channelId.trim()}:${track.fileId.trim()}`;
  if (track.localPath) {
    const path = normalizeLocalPath(track.localPath);
    return path ? `local:${path}` : null;
  }
  return null;
}

export interface TrackTags {
  title?: string;
  artist?: string;
  album?: string;
}

/** The track as the player endpoints take it (null when it has no server key). */
export function toPlayerTrack(track: AudioTrack, tags: TrackTags = {}, durationSeconds?: number): PlayerTrackInput | null {
  if (!playerKey(track)) return null;
  const duration = durationSeconds && Number.isFinite(durationSeconds) && durationSeconds > 0 ? durationSeconds : track.durationSeconds;
  const common = {
    fileName: track.title,
    channelName: track.source ?? null,
    title: tags.title || track.title,
    artist: tags.artist || track.artist || null,
    album: tags.album || track.album || null,
    duration: duration ?? null,
    fileSize: track.size ?? null
  };
  if (track.channelId && track.fileId) {
    return { ...common, channelId: track.channelId, fileId: track.fileId, isLocalFile: false, filePath: null };
  }
  const path = normalizeLocalPath(track.localPath ?? '');
  return { ...common, channelId: null, fileId: null, isLocalFile: true, filePath: path, fileName: path.split('/').pop() || track.title };
}

/** A play counts for the history after this many seconds listened, or at the end of the track. */
export const HISTORY_MIN_SECONDS = 30;

/**
 * Decides when the current play goes to the history: once per play, after
 * 30 s actually listened (seeking does not count) or when the end is reached.
 * A new play starts when the queue entry changes or the same entry starts
 * over after the end (repeat one).
 */
export class PlayTracker {
  private qid: string | null = null;
  private listened = 0;
  private lastPosition = 0;
  private recorded = false;

  /** Feed every position update; true exactly once per play, when it should be recorded. */
  update(qid: string | null, position: number, duration: number, playing: boolean): boolean {
    if (qid !== this.qid) {
      this.qid = qid;
      this.listened = 0;
      this.lastPosition = position;
      this.recorded = false;
      return false;
    }
    if (!qid) return false;

    const delta = position - this.lastPosition;
    // Started over from the end (repeat one, or "previous" at the end): a new play
    if (this.recorded && position < 2 && duration > 0 && this.lastPosition >= duration - 2) {
      this.listened = 0;
      this.recorded = false;
    } else if (playing && delta > 0 && delta <= 2) {
      // Normal playback ticks; larger jumps are seeks
      this.listened += delta;
    }
    this.lastPosition = position;

    if (this.recorded) return false;
    const atEnd = duration > 0 && position >= duration - 1 && this.listened > 0;
    if (this.listened >= HISTORY_MIN_SECONDS || atEnd) {
      this.recorded = true;
      return true;
    }
    return false;
  }

  /** Whether the current play reached the end (for `completed`). */
  static completed(position: number, duration: number): boolean {
    return duration > 0 && position >= duration - 1;
  }
}
