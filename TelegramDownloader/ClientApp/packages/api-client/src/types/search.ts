// Global search across every indexed channel and the local storage: GET /api/v1/search
// (mirrors ApiGlobalSearchResultDto / ApiSearchChannelGroupDto of Controllers/Api/V1/SearchController.cs)
import type { ApiFileDto, BrowseFilter } from './files';

/** `scope` values of `GET /api/v1/search`. */
export type GlobalSearchScope = 'all' | 'channels' | 'local';

/** Query of `GET /api/v1/search`. */
export interface GlobalSearchQuery {
  /** Text to look for (at least 2 characters, case-insensitive substring of the file name). */
  q: string;
  /** `all` (default), `audio`, `video`, `photo`, `document` or `archive`. */
  filter?: BrowseFilter | (string & {});
  /** `all` (default), `channels` or `local`. */
  scope?: GlobalSearchScope;
  /** 1-500 (default 200), applied separately to the channel results and to the local results. */
  limit?: number;
}

/** Matches of one channel. */
export interface ApiSearchChannelGroupDto {
  channelId: string;
  /** Falls back to the id when the name cannot be resolved. */
  channelName: string;
  files: ApiFileDto[];
}

/** Answer of `GET /api/v1/search`. */
export interface ApiGlobalSearchResultDto {
  /** Matches per channel, ordered by channel name; only channels with matches appear. */
  channels: ApiSearchChannelGroupDto[];
  /** Matches in the server local storage. */
  local: ApiFileDto[];
  /** More matches existed than `limit`. */
  truncated: boolean;
}
