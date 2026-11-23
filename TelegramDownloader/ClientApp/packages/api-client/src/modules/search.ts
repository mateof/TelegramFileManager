import type { HttpClient, Paged } from '../http';
import type { ApiFileDto, FileSearchQuery } from '../types/files';
import type { ChannelId } from '../types/channels';
import type { ApiGlobalSearchResultDto, GlobalSearchQuery } from '../types/search';

/** File search, inside one channel or across every indexed channel. */
export function createSearchApi(http: HttpClient) {
  return {
    /** GET /api/v1/channels/{channelId}/files/search (paged): same as `files.search`. */
    inChannel: (channelId: ChannelId, query: FileSearchQuery, signal?: AbortSignal): Promise<Paged<ApiFileDto>> =>
      http.getPaged<ApiFileDto>(`/api/v1/channels/${encodeURIComponent(channelId)}/files/search`, { query: { ...query }, signal }),
    /** GET /api/v1/search: file names in every indexed channel (grouped by channel) and in the local storage. */
    global: (query: GlobalSearchQuery, signal?: AbortSignal) =>
      http.get<ApiGlobalSearchResultDto>('/api/v1/search', {
        query: { q: query.q, filter: query.filter, scope: query.scope, limit: query.limit },
        signal
      })
  };
}

export type SearchApi = ReturnType<typeof createSearchApi>;
