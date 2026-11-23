import type { HttpClient, Paged } from '../http';
import type {
  CreateStrmRequest,
  ImportSharedRequest,
  ShareExportQuery,
  SharedCollectionDto,
  ShareFilesModel,
  SharesListQuery
} from '../types/shares';
import type { ChannelId } from '../types/channels';

const BASE = '/api/v1/shares';
const enc = encodeURIComponent;

/** Shared collections between instances, and the Emby/Kodi `.strm` export. */
export function createSharesApi(http: HttpClient) {
  return {
    /** GET /api/v1/shares (paged, newest first) */
    list: (query: SharesListQuery = {}, signal?: AbortSignal): Promise<Paged<SharedCollectionDto>> =>
      http.getPaged<SharedCollectionDto>(BASE, { query: { ...query }, signal }),
    /** GET /api/v1/shares/{id} */
    get: (id: string, signal?: AbortSignal) => http.get<SharedCollectionDto>(`${BASE}/${enc(id)}`, { signal }),
    /** GET /api/v1/shares/export: builds a share payload for a channel folder. */
    export: (query: ShareExportQuery, signal?: AbortSignal) =>
      http.get<ShareFilesModel>(`${BASE}/export`, { query: { ...query }, signal }),
    /** POST /api/v1/shares/import (202): imports a payload from another instance in the background. */
    import: (body: ImportSharedRequest) => http.post<void>(`${BASE}/import`, body),
    /** DELETE /api/v1/shares/{id} */
    delete: (id: string) => http.delete<void>(`${BASE}/${enc(id)}`),
    /**
     * POST /api/v1/shares/strm?channelId=: writes `.strm` files. Answers the destination folder when
     * `destinationFolder` is set, otherwise a relative URL to the zip archive.
     */
    createStrm: (channelId: ChannelId, body: CreateStrmRequest = {}, signal?: AbortSignal) =>
      http.post<string>(`${BASE}/strm`, body, { query: { channelId: String(channelId) }, signal })
  };
}

export type SharesApi = ReturnType<typeof createSharesApi>;
