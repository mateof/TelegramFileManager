import type { HttpClient, Paged } from '../http';
import type {
  ApiLibraryFileDto,
  ApiLibraryFolderDto,
  ApiLibraryGenreDto,
  ApiLibraryItemDetailDto,
  ApiLibraryItemDto,
  ApiLibraryProviderDto,
  ApiLibraryScanStateDto,
  ApiLibraryStatsDto,
  ApiProviderCandidateDto,
  ApiWatchStateDto,
  LibraryAssignFolderRequest,
  LibraryFilesQuery,
  LibraryIdentifyItemRequest,
  LibraryItemsQuery,
  LibraryItemWatchedRequest,
  LibraryKind,
  LibraryLimitQuery,
  LibraryMatchFileRequest,
  LibraryProviderSearchQuery,
  LibraryRecentQuery,
  LibraryScanRequest,
  LibraryWatchUpdateRequest
} from '../types/library';

const BASE = '/api/v1/library';
const enc = encodeURIComponent;
const fileRoute = (channelId: number | string, fileId: string) => `${enc(channelId)}/${enc(fileId)}`;

/**
 * Media library (movies and series identified from channel video files) and playback progress.
 * Most calls answer 503 `library_disabled` while the library is switched off.
 */
export function createLibraryApi(http: HttpClient) {
  return {
    // ----- catalogue -----
    /** GET /api/v1/library/items (paged) */
    items: (query: LibraryItemsQuery = {}, signal?: AbortSignal): Promise<Paged<ApiLibraryItemDto>> =>
      http.getPaged<ApiLibraryItemDto>(`${BASE}/items`, { query: { ...query }, signal }),
    /** GET /api/v1/library/items/{id} */
    item: (id: string, signal?: AbortSignal) => http.get<ApiLibraryItemDetailDto>(`${BASE}/items/${enc(id)}`, { signal }),
    /** GET /api/v1/library/continue: files in progress, most recent first. */
    continue: (query: LibraryLimitQuery = {}, signal?: AbortSignal) =>
      http.get<ApiLibraryFileDto[]>(`${BASE}/continue`, { query: { ...query }, signal }),
    /** GET /api/v1/library/recent */
    recent: (query: LibraryRecentQuery = {}, signal?: AbortSignal) =>
      http.get<ApiLibraryItemDto[]>(`${BASE}/recent`, { query: { ...query }, signal }),
    /** GET /api/v1/library/genres */
    genres: (kind?: LibraryKind, signal?: AbortSignal) =>
      http.get<ApiLibraryGenreDto[]>(`${BASE}/genres`, { query: { kind }, signal }),
    /** GET /api/v1/library/stats */
    stats: (signal?: AbortSignal) => http.get<ApiLibraryStatsDto>(`${BASE}/stats`, { signal }),
    /** GET /api/v1/library/files (paged) */
    files: (query: LibraryFilesQuery = {}, signal?: AbortSignal): Promise<Paged<ApiLibraryFileDto>> =>
      http.getPaged<ApiLibraryFileDto>(`${BASE}/files`, { query: { ...query }, signal }),
    /** GET /api/v1/library/files/{channelId}/{fileId} */
    file: (channelId: number | string, fileId: string, signal?: AbortSignal) =>
      http.get<ApiLibraryFileDto>(`${BASE}/files/${fileRoute(channelId, fileId)}`, { signal }),
    /**
     * GET /api/v1/library/images/{key}?size=: cached poster/backdrop/still. The DTOs already carry
     * absolute URLs; use this only to request another size for a known key.
     */
    imageUrl: (key: string, size?: string) => http.url(`${BASE}/images/${enc(key)}`, { size }),

    /** GET /api/v1/library/channel-folders?channelId=: folder paths of a channel index (for folder rules). */
    channelFolders: (channelId: number | string, signal?: AbortSignal) =>
      http.get<string[]>(`${BASE}/channel-folders`, { query: { channelId: String(channelId) }, signal }),

    // ----- scan -----
    /** POST /api/v1/library/scan (202; 409 `already_running`) */
    scan: (body: LibraryScanRequest = {}) => http.post<void>(`${BASE}/scan`, body),
    /** GET /api/v1/library/scan: null before the first scan. */
    scanState: (signal?: AbortSignal) => http.get<ApiLibraryScanStateDto | null>(`${BASE}/scan`, { signal }),
    /** DELETE /api/v1/library/scan */
    cancelScan: () => http.delete<void>(`${BASE}/scan`),

    // ----- identification -----
    /** GET /api/v1/library/providers/search */
    searchProviders: (query: LibraryProviderSearchQuery, signal?: AbortSignal) =>
      http.get<ApiProviderCandidateDto[]>(`${BASE}/providers/search`, { query: { ...query }, signal }),
    /** GET /api/v1/library/providers */
    providers: (signal?: AbortSignal) => http.get<ApiLibraryProviderDto[]>(`${BASE}/providers`, { signal }),
    /** PUT /api/v1/library/files/{channelId}/{fileId}/match */
    matchFile: (channelId: number | string, fileId: string, body: LibraryMatchFileRequest, signal?: AbortSignal) =>
      http.put<ApiLibraryFileDto>(`${BASE}/files/${fileRoute(channelId, fileId)}/match`, body, { signal }),
    /** DELETE /api/v1/library/files/{channelId}/{fileId}/match */
    unmatchFile: (channelId: number | string, fileId: string) =>
      http.delete<ApiLibraryFileDto>(`${BASE}/files/${fileRoute(channelId, fileId)}/match`),
    /** POST /api/v1/library/files/{channelId}/{fileId}/ignore */
    ignoreFile: (channelId: number | string, fileId: string) =>
      http.post<ApiLibraryFileDto>(`${BASE}/files/${fileRoute(channelId, fileId)}/ignore`),
    /** DELETE /api/v1/library/files/{channelId}/{fileId}/ignore */
    unignoreFile: (channelId: number | string, fileId: string) =>
      http.delete<ApiLibraryFileDto>(`${BASE}/files/${fileRoute(channelId, fileId)}/ignore`),
    /** GET /api/v1/library/folders: the title assigned to a folder and how many videos it has. */
    folder: (channelId: number | string, path: string, signal?: AbortSignal) =>
      http.get<ApiLibraryFolderDto>(`${BASE}/folders`, { query: { channelId: String(channelId), path }, signal }),
    /** PUT /api/v1/library/folders/series: every video of the folder is an episode of one series. */
    assignFolder: (body: LibraryAssignFolderRequest) => http.put<ApiLibraryFolderDto>(`${BASE}/folders/series`, body),
    /** DELETE /api/v1/library/folders/series: removes the rule of the folder. */
    unassignFolder: (channelId: number | string, path: string) =>
      http.delete<void>(`${BASE}/folders/series`, undefined, { query: { channelId: String(channelId), path } }),
    /** PUT /api/v1/library/items/{id}/identify */
    identifyItem: (id: string, body: LibraryIdentifyItemRequest, signal?: AbortSignal) =>
      http.put<ApiLibraryItemDetailDto>(`${BASE}/items/${enc(id)}/identify`, body, { signal }),
    /** POST /api/v1/library/items/{id}/refresh: re-reads the metadata from the provider. */
    refreshItem: (id: string, signal?: AbortSignal) =>
      http.post<ApiLibraryItemDetailDto>(`${BASE}/items/${enc(id)}/refresh`, undefined, { signal }),

    // ----- watch state -----
    /** GET /api/v1/library/watch/{channelId}/{fileId}: null when never played. */
    watch: (channelId: number | string, fileId: string, signal?: AbortSignal) =>
      http.get<ApiWatchStateDto | null>(`${BASE}/watch/${fileRoute(channelId, fileId)}`, { signal }),
    /** PUT /api/v1/library/watch/{channelId}/{fileId}: reports the playback position. */
    updateWatch: (channelId: number | string, fileId: string, body: LibraryWatchUpdateRequest) =>
      http.put<ApiWatchStateDto>(`${BASE}/watch/${fileRoute(channelId, fileId)}`, body),
    /** DELETE /api/v1/library/watch/{channelId}/{fileId} */
    deleteWatch: (channelId: number | string, fileId: string) =>
      http.delete<void>(`${BASE}/watch/${fileRoute(channelId, fileId)}`),
    /** POST /api/v1/library/watch/{channelId}/{fileId}/watched */
    markWatched: (channelId: number | string, fileId: string) =>
      http.post<ApiWatchStateDto>(`${BASE}/watch/${fileRoute(channelId, fileId)}/watched`),
    /** DELETE /api/v1/library/watch/{channelId}/{fileId}/watched */
    markUnwatched: (channelId: number | string, fileId: string) =>
      http.delete<ApiWatchStateDto>(`${BASE}/watch/${fileRoute(channelId, fileId)}/watched`),
    /** POST /api/v1/library/items/{id}/watched */
    markItemWatched: (id: string, body: LibraryItemWatchedRequest = {}) =>
      http.post<ApiLibraryItemDetailDto>(`${BASE}/items/${enc(id)}/watched`, body),
    /** DELETE /api/v1/library/items/{id}/watched?season= */
    markItemUnwatched: (id: string, season?: number) =>
      http.delete<ApiLibraryItemDetailDto>(`${BASE}/items/${enc(id)}/watched`, undefined, { query: { season } }),
    /** GET /api/v1/library/watch/history */
    history: (query: LibraryLimitQuery = {}, signal?: AbortSignal) =>
      http.get<ApiLibraryFileDto[]>(`${BASE}/watch/history`, { query: { ...query }, signal })
  };
}

export type LibraryApi = ReturnType<typeof createLibraryApi>;
