// Media library (movies and series): /api/v1/library (mirrors Models/Api/LibraryDtos.cs and the
// string constants of Models/Library/LibraryModels.cs)
import type { PagedQuery } from '../http';
import type { ApiFileDto } from './files';

/** Kind of a library item. */
export const LibraryKind = {
  Movie: 'movie',
  Series: 'series'
} as const;

export type LibraryKind = (typeof LibraryKind)[keyof typeof LibraryKind];

/** Identification status of a library file. */
export const LibraryFileStatus = {
  Matched: 'matched',
  Review: 'review',
  Unmatched: 'unmatched',
  Ignored: 'ignored'
} as const;

export type LibraryFileStatus = (typeof LibraryFileStatus)[keyof typeof LibraryFileStatus];

/** How a library file was identified. */
export const LibraryMatchSource = {
  Auto: 'auto',
  Manual: 'manual'
} as const;

export type LibraryMatchSource = (typeof LibraryMatchSource)[keyof typeof LibraryMatchSource];

/** Playback progress of one file. */
export interface ApiWatchStateDto {
  channelId: number;
  fileId: string;
  fileName: string | null;
  itemId: string | null;
  season: number | null;
  episode: number | null;
  positionMs: number;
  durationMs: number;
  /** 0-1 fraction of the duration already played. */
  progress: number;
  completed: boolean;
  playCount: number;
  /** ISO date. */
  firstPlayedAt: string;
  /** ISO date. */
  lastPlayedAt: string;
}

/** Watch state of a whole item (a movie or a series). */
export interface ApiWatchSummaryDto {
  /** Movie: watched. Series: every available episode watched. */
  completed: boolean;
  inProgress: boolean;
  /** Position of the most recently played file, when in progress. */
  positionMs: number;
  durationMs: number;
  progress: number;
  episodesTotal: number;
  episodesWatched: number;
  /** ISO date. */
  lastPlayedAt: string | null;
}

/** What the file name parser understood. */
export interface ApiParsedNameDto {
  title: string;
  year: number | null;
  season: number | null;
  episode: number | null;
  isSeries: boolean;
  source: string;
}

/** A channel video file as seen by the library. */
export interface ApiLibraryFileDto {
  channelId: number;
  fileId: string;
  fileName: string;
  folderPath: string;
  size: number;
  sizeText: string;
  /** `matched`, `review`, `unmatched` or `ignored`. */
  status: LibraryFileStatus | (string & {});
  confidence: number;
  /** `auto` or `manual`. */
  matchSource: LibraryMatchSource | (string & {});
  locked: boolean;
  parsed: ApiParsedNameDto;
  itemId: string | null;
  kind: LibraryKind | (string & {}) | null;
  season: number | null;
  episode: number | null;
  /** The regular file DTO, with its stream and download URLs. */
  file: ApiFileDto;
  watch: ApiWatchStateDto | null;
  /** The item the file belongs to. Present in flat lists (continue, review). */
  item: ApiLibraryItemDto | null;
  /** ISO date. */
  scannedAt: string;
  error: string | null;
}

/** A movie or a series of the library. */
export interface ApiLibraryItemDto {
  id: string;
  /** `movie` or `series`. */
  kind: LibraryKind | (string & {});
  title: string;
  originalTitle: string | null;
  year: number | null;
  overview: string | null;
  genres: string[];
  rating: number | null;
  voteCount: number | null;
  /** Minutes. */
  runtime: number | null;
  /** Absolute URL served by this server (cached copy of the provider image). */
  posterUrl: string | null;
  backdropUrl: string | null;
  provider: string;
  providerId: string;
  imdbId: string | null;
  externalIds: Record<string, string>;
  locked: boolean;
  fileCount: number;
  channelIds: number[];
  seasonCount: number;
  /** ISO date. */
  addedAt: string;
  watch: ApiWatchSummaryDto;
}

/** A library item with its files, seasons and what to play next. */
export interface ApiLibraryItemDetailDto extends ApiLibraryItemDto {
  tagline: string | null;
  status: string | null;
  /** Movies: the files (versions) of the movie. */
  files: ApiLibraryFileDto[];
  /** Series: seasons that have at least one file, with their episodes. */
  seasons: ApiLibrarySeasonDto[];
  /** Series: the episode to play next (the one in progress, or the first unwatched). */
  nextUp: ApiLibraryEpisodeDto | null;
  /** The file to resume, when something is in progress. */
  resume: ApiLibraryFileDto | null;
}

/** A season of a series. */
export interface ApiLibrarySeasonDto {
  number: number;
  name: string | null;
  overview: string | null;
  posterUrl: string | null;
  episodeCount: number | null;
  /** Episodes that have files, in order. */
  episodes: ApiLibraryEpisodeDto[];
  episodesWatched: number;
}

/** An episode of a series. */
export interface ApiLibraryEpisodeDto {
  season: number;
  number: number;
  title: string | null;
  overview: string | null;
  stillUrl: string | null;
  /** ISO date. */
  airDate: string | null;
  runtime: number | null;
  rating: number | null;
  files: ApiLibraryFileDto[];
  /** Progress of the most recently played file of the episode. */
  watch: ApiWatchStateDto | null;
  completed: boolean;
}

/** A match candidate returned by a metadata provider search. */
export interface ApiProviderCandidateDto {
  provider: string;
  providerId: string;
  kind: LibraryKind | (string & {});
  title: string;
  originalTitle: string | null;
  year: number | null;
  overview: string | null;
  posterUrl: string | null;
  imdbId: string | null;
}

/** A metadata provider and how it is configured. */
export interface ApiLibraryProviderDto {
  id: string;
  name: string;
  website: string;
  keyUrl: string;
  requiresKey: boolean;
  supportsEpisodes: boolean;
  supportsImages: boolean;
  supportsLanguage: boolean;
  enabled: boolean;
  hasKey: boolean;
  /** Last characters of the key, for display. */
  apiKeyMasked: string | null;
  priority: number;
}

/** Provider settings as sent by a client (the key is optional: omit it to keep the current one). */
export interface LibraryProviderConfigDto {
  id: string;
  enabled?: boolean | null;
  apiKey?: string | null;
  priority?: number | null;
}

/** State of the running or last library scan. */
export interface ApiLibraryScanStateDto {
  running: boolean;
  cancelled: boolean;
  /** ISO date. */
  startedAt: string | null;
  /** ISO date. */
  finishedAt: string | null;
  scope: string;
  force: boolean;
  channelsTotal: number;
  channelsScanned: number;
  currentChannel: string | null;
  filesSeen: number;
  /** Files that have to be identified; 0 until every channel index has been read. Absent on older servers. */
  filesToProcess?: number;
  filesProcessed?: number;
  filesNew: number;
  filesRemoved: number;
  matched: number;
  review: number;
  unmatched: number;
  failed: number;
  error: string | null;
}

/** Library settings and counters. */
export interface ApiLibraryStatsDto {
  enabled: boolean;
  language: string;
  autoScan: boolean;
  watchedThreshold: number;
  providers: ApiLibraryProviderDto[];
  /** True when at least one enabled provider has its key. */
  ready: boolean;
  movies: number;
  series: number;
  /** Files by status: matched, review, unmatched, ignored. */
  files: Record<string, number>;
  inProgress: number;
  scan: ApiLibraryScanStateDto | null;
}

/** A genre and how many items carry it. */
export interface ApiLibraryGenreDto {
  name: string;
  count: number;
}

// ----- request bodies -----

/** Body of `POST /api/v1/library/scan`. */
export interface LibraryScanRequest {
  /** Scan only this channel. Omit for every channel with an index. */
  channelId?: string | null;
  /** Re-identify files that were already matched automatically. Manual matches are never touched. */
  force?: boolean;
}

/** Body of `PUT /api/v1/library/files/{channelId}/{fileId}/match`. */
export interface LibraryMatchFileRequest {
  provider: string;
  providerId: string;
  /** `movie` or `series`. */
  kind: LibraryKind;
  season?: number | null;
  episode?: number | null;
}

/** Body of `PUT /api/v1/library/items/{id}/identify`. */
export interface LibraryIdentifyItemRequest {
  provider: string;
  providerId: string;
  /** Defaults to the item's current kind. */
  kind?: LibraryKind | null;
}

/** Body of `PUT /api/v1/library/watch/{channelId}/{fileId}`. */
export interface LibraryWatchUpdateRequest {
  positionMs: number;
  durationMs: number;
  /** Force the watched flag (e.g. the player reached the end). */
  completed?: boolean | null;
}

/** Body of `POST /api/v1/library/items/{id}/watched`. */
export interface LibraryItemWatchedRequest {
  /** Series: limit to one season. */
  season?: number | null;
}

// ----- queries -----

/** `sortBy` values of `GET /api/v1/library/items`. */
export type LibraryItemsSortBy = 'title' | 'year' | 'added' | 'rating' | 'lastPlayed';

/** Query of `GET /api/v1/library/items`. `sortBy`: `title` (default), `year`, `added`, `rating`, `lastPlayed`. */
export interface LibraryItemsQuery extends PagedQuery {
  kind?: LibraryKind;
  search?: string;
  genre?: string;
  /** `review` keeps only items that have a file whose identification deserves a look. */
  status?: string;
  includeHidden?: boolean;
}

/** Query of `GET /api/v1/library/files`. */
export interface LibraryFilesQuery extends PagedQuery {
  status?: LibraryFileStatus;
  channelId?: number;
  search?: string;
}

/** Query of `GET /api/v1/library/continue` and `/watch/history`. */
export interface LibraryLimitQuery {
  /** 1-500 (defaults: continue 50, history 100). */
  limit?: number;
  includeHidden?: boolean;
}

/** Query of `GET /api/v1/library/recent`. */
export interface LibraryRecentQuery {
  kind?: LibraryKind;
  /** 1-500 (default 30). */
  limit?: number;
  includeHidden?: boolean;
}

/** Query of `GET /api/v1/library/providers/search`. */
export interface LibraryProviderSearchQuery {
  q?: string;
  kind?: LibraryKind;
  year?: number;
  imdbId?: string;
  /** Provider id; omit to search every enabled provider. */
  provider?: string;
}
