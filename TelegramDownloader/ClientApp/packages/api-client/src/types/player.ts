// Shared player data: /api/v1/player (capability `player.sync`). Mirrors Models/Api/PlayerDtos.cs.

/**
 * A track of the player data. `key` is `channel:{channelId}:{fileId}` or `local:{path}`
 * (path relative to the local root, `/` separators, no leading slash); the server
 * computes it from the ids/path, so it can be left out when writing.
 */
export interface ApiPlayerTrackDto {
  key: string;
  channelId: string | null;
  fileId: string | null;
  isLocalFile: boolean;
  filePath: string | null;
  fileName: string;
  channelName?: string | null;
  title?: string | null;
  artist?: string | null;
  album?: string | null;
  /** Seconds. */
  duration?: number | null;
  /** Bytes. */
  fileSize?: number | null;
}

/** A track as sent in a body: the server fills `key`, a missing `fileName` comes from the path. */
export type PlayerTrackInput = Partial<ApiPlayerTrackDto> &
  ({ channelId: string; fileId: string } | { isLocalFile: true; filePath: string } | { key: string });

export interface ApiPlayerFavoriteDto extends ApiPlayerTrackDto {
  /** ISO date (UTC) of the first time it was marked; re-adding keeps it. */
  addedAt: string;
}

export interface ApiPlayerFavoritesPageDto {
  items: ApiPlayerFavoriteDto[];
  total: number;
  page: number;
  pageSize: number;
}

export interface PlayerHistoryWriteRequest {
  track: PlayerTrackInput;
  /** ISO date, default now. */
  playedAt?: string;
  positionSeconds?: number;
  completed?: boolean;
}

export interface ApiPlayerHistoryEntryDto {
  id: string;
  track: ApiPlayerTrackDto;
  /** ISO date (UTC). */
  playedAt: string;
  positionSeconds: number | null;
  completed: boolean;
}

export interface ApiPlayerHistoryPageDto {
  items: ApiPlayerHistoryEntryDto[];
  hasMore: boolean;
}

export interface ApiPlayerTopTrackDto {
  track: ApiPlayerTrackDto;
  playCount: number;
  /** ISO date (UTC). */
  lastPlayedAt: string;
}

export interface PlayerResumeWriteRequest {
  track: PlayerTrackInput;
  positionSeconds: number;
  durationSeconds: number;
}

export interface ApiPlayerResumeDto {
  track: ApiPlayerTrackDto;
  positionSeconds: number;
  durationSeconds: number;
  /** ISO date (UTC). */
  updatedAt: string;
  /** Only in the answer of `PUT /resume`: the position counted as finished and the entry was deleted. */
  finished: boolean;
}

export type PlayerDataKind = 'favorites' | 'history' | 'resume';

/** `PlayerDataChanged` message of /hubs/events. `key` is null when a whole list changed (history cleared). */
export interface PlayerDataChangedEvent {
  kind: PlayerDataKind;
  key: string | null;
}
