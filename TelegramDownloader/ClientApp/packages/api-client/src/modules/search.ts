import type { HttpClient, Paged } from '../http';
import type { ApiFileDto, FileSearchQuery } from '../types/files';
import type { ChannelId } from '../types/channels';
import type { GlobalSearchQuery, GlobalSearchResultDto } from '../types/search';

/** File search, inside one channel or across every indexed channel. */
export function createSearchApi(http: HttpClient) {
  return {
    /** GET /api/v1/channels/{channelId}/files/search (paged): same as `files.search`. */
    inChannel: (channelId: ChannelId, query: FileSearchQuery, signal?: AbortSignal): Promise<Paged<ApiFileDto>> =>
      http.getPaged<ApiFileDto>(`/api/v1/channels/${encodeURIComponent(channelId)}/files/search`, { query: { ...query }, signal }),
    /** GET /api/v1/search (paged) */
    // PROVISIONAL: contract owned by the API agent, align at merge
    global: (query: GlobalSearchQuery, signal?: AbortSignal): Promise<Paged<GlobalSearchResultDto>> =>
      http.getPaged<GlobalSearchResultDto>('/api/v1/search', {
        query: { q: query.q, page: query.page, pageSize: query.pageSize, type: query.type, channelId: query.channelId },
        signal
      })
  };
}

export type SearchApi = ReturnType<typeof createSearchApi>;
