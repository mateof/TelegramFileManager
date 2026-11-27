import type { HttpClient } from '../http';
import type {
  ApiPlayerFavoriteDto,
  ApiPlayerFavoritesPageDto,
  ApiPlayerHistoryEntryDto,
  ApiPlayerHistoryPageDto,
  ApiPlayerResumeDto,
  ApiPlayerTopTrackDto,
  PlayerHistoryWriteRequest,
  PlayerResumeWriteRequest,
  PlayerTrackInput
} from '../types/player';

const BASE = '/api/v1/player';

/**
 * Favourites, play history and resume positions shared by every device
 * (capability `player.sync`). Every write is announced as `PlayerDataChanged`
 * on /hubs/events.
 */
export function createPlayerApi(http: HttpClient) {
  return {
    /** GET /api/v1/player/favorites (newest first) */
    favorites: (query: { page?: number; pageSize?: number } = {}, signal?: AbortSignal) =>
      http.get<ApiPlayerFavoritesPageDto>(`${BASE}/favorites`, { query: { ...query }, signal }),
    /** GET /api/v1/player/favorites/keys */
    favoriteKeys: (signal?: AbortSignal) => http.get<string[]>(`${BASE}/favorites/keys`, { signal }),
    /** PUT /api/v1/player/favorites (idempotent) */
    addFavorite: (track: PlayerTrackInput) => http.put<ApiPlayerFavoriteDto>(`${BASE}/favorites`, track),
    /** DELETE /api/v1/player/favorites?key= (204, also when it was not a favourite) */
    removeFavorite: (key: string) => http.delete<void>(`${BASE}/favorites`, undefined, { query: { key } }),

    /** POST /api/v1/player/history (201) */
    addHistory: (body: PlayerHistoryWriteRequest) => http.post<ApiPlayerHistoryEntryDto>(`${BASE}/history`, body),
    /** GET /api/v1/player/history (newest first; page with `before` = the last `playedAt`) */
    history: (query: { limit?: number; before?: string } = {}, signal?: AbortSignal) =>
      http.get<ApiPlayerHistoryPageDto>(`${BASE}/history`, { query: { ...query }, signal }),
    /** GET /api/v1/player/history/top */
    topPlayed: (query: { limit?: number; days?: number } = {}, signal?: AbortSignal) =>
      http.get<ApiPlayerTopTrackDto[]>(`${BASE}/history/top`, { query: { ...query }, signal }),
    /** DELETE /api/v1/player/history (204) */
    clearHistory: () => http.delete<void>(`${BASE}/history`),

    /** PUT /api/v1/player/resume (`finished: true` when it was deleted instead) */
    saveResume: (body: PlayerResumeWriteRequest) => http.put<ApiPlayerResumeDto>(`${BASE}/resume`, body),
    /** GET /api/v1/player/resume?key= (404 when there is none) */
    resume: (key: string, signal?: AbortSignal) => http.get<ApiPlayerResumeDto>(`${BASE}/resume`, { query: { key }, signal }),
    /** GET /api/v1/player/resume/list (most recent first) */
    resumeList: (query: { limit?: number } = {}, signal?: AbortSignal) =>
      http.get<ApiPlayerResumeDto[]>(`${BASE}/resume/list`, { query: { ...query }, signal }),
    /** DELETE /api/v1/player/resume?key= (204) */
    removeResume: (key: string) => http.delete<void>(`${BASE}/resume`, undefined, { query: { key } })
  };
}

export type PlayerApi = ReturnType<typeof createPlayerApi>;
