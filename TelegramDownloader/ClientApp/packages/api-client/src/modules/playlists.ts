import type { HttpClient, Paged } from '../http';
import type {
  PlaylistDownloadQuery,
  PlaylistListQuery,
  PlaylistModel,
  PlaylistTrackWriteRequest,
  PlaylistWriteRequest
} from '../types/playlists';

const BASE = '/api/v1/playlists';
const enc = encodeURIComponent;

/** Playlists stored on the server (channel tracks and local files). */
export function createPlaylistsApi(http: HttpClient) {
  return {
    /** GET /api/v1/playlists (paged) */
    list: (query: PlaylistListQuery = {}, signal?: AbortSignal): Promise<Paged<PlaylistModel>> =>
      http.getPaged<PlaylistModel>(BASE, { query: { ...query }, signal }),
    /** GET /api/v1/playlists/{id} */
    get: (id: string, signal?: AbortSignal) => http.get<PlaylistModel>(`${BASE}/${enc(id)}`, { signal }),
    /** POST /api/v1/playlists (201) */
    create: (body: PlaylistWriteRequest) => http.post<PlaylistModel>(BASE, body),
    /** PUT /api/v1/playlists/{id}: blank name/description keep the current ones, a non-empty `tracks` replaces the list. */
    update: (id: string, body: Partial<PlaylistWriteRequest>) => http.put<PlaylistModel>(`${BASE}/${enc(id)}`, body),
    /** DELETE /api/v1/playlists/{id} */
    delete: (id: string) => http.delete<void>(`${BASE}/${enc(id)}`),
    /** POST /api/v1/playlists/{id}/tracks: appends a track. */
    addTrack: (id: string, track: PlaylistTrackWriteRequest) => http.post<PlaylistModel>(`${BASE}/${enc(id)}/tracks`, track),
    /** DELETE /api/v1/playlists/{id}/tracks/{fileId} */
    removeTrack: (id: string, fileId: string) => http.delete<PlaylistModel>(`${BASE}/${enc(id)}/tracks/${enc(fileId)}`),
    /** PUT /api/v1/playlists/{id}/tracks/order: body is the file ids in the new order. */
    reorder: (id: string, orderedFileIds: string[]) => http.put<PlaylistModel>(`${BASE}/${enc(id)}/tracks/order`, orderedFileIds),
    /** POST /api/v1/playlists/{id}/download (202): downloads every track to the local storage. */
    download: (id: string, query: PlaylistDownloadQuery = {}) =>
      http.post<void>(`${BASE}/${enc(id)}/download`, undefined, { query: { ...query } })
  };
}

export type PlaylistsApi = ReturnType<typeof createPlaylistsApi>;
