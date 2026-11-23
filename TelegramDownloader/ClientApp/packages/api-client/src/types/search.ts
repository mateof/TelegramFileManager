// Global search across every indexed channel: GET /api/v1/search
// PROVISIONAL: contract owned by the API agent, align at merge
import type { ApiFileDto, BrowseFilter } from './files';

/** Query of `GET /api/v1/search`. */
// PROVISIONAL: contract owned by the API agent, align at merge
export interface GlobalSearchQuery {
  /** Text to look for (case-insensitive substring of the file name). */
  q: string;
  page?: number;
  pageSize?: number;
  /** Restrict to a category (same values as the browse `filter`). */
  type?: BrowseFilter | (string & {});
  /** Restrict to one channel. */
  channelId?: string;
}

/** One hit of the global search: a channel file plus the channel it lives in. */
// PROVISIONAL: contract owned by the API agent, align at merge
export interface GlobalSearchResultDto extends ApiFileDto {
  channelId: string;
  channelName: string | null;
}
