import type { HttpClient, Paged } from '../http';
import type {
  DatabaseStatsDto,
  DeleteLogsQuery,
  FfmpegInfoDto,
  ImageCacheInfoDto,
  LogEntryDto,
  LogQuery,
  PathAnalysisDto,
  SaveSetupRequest,
  ServerInfoDto,
  SetupStatusDto,
  SetupTestResultDto,
  SystemMetricsDto,
  TestMongoRequest,
  TestTelegramRequest,
  VersionCheckDto
} from '../types/system';

const BASE = '/api/v1/system';
const enc = encodeURIComponent;

/** Server identity, health, metrics, logs and channel database maintenance. */
export function createSystemApi(http: HttpClient) {
  return {
    /** GET /api/v1/system/ping: answers `"pong"`. */
    ping: (signal?: AbortSignal) => http.get<string>(`${BASE}/ping`, { signal }),
    /** GET /api/v1/system/info */
    info: (signal?: AbortSignal) => http.get<ServerInfoDto>(`${BASE}/info`, { signal }),
    /** GET /api/v1/system/setup */
    setup: (signal?: AbortSignal) => http.get<SetupStatusDto>(`${BASE}/setup`, { signal }),
    /** GET /api/v1/system/metrics */
    metrics: (signal?: AbortSignal) => http.get<SystemMetricsDto>(`${BASE}/metrics`, { signal }),
    /** GET /api/v1/system/logs (paged) */
    logs: (query: LogQuery = {}, signal?: AbortSignal): Promise<Paged<LogEntryDto>> =>
      http.getPaged<LogEntryDto>(`${BASE}/logs`, { query: { ...query }, signal }),
    /** GET /api/v1/system/logs/loggers */
    logLoggers: (signal?: AbortSignal) => http.get<string[]>(`${BASE}/logs/loggers`, { signal }),
    /** GET /api/v1/system/logs/versions */
    logVersions: (signal?: AbortSignal) => http.get<string[]>(`${BASE}/logs/versions`, { signal }),
    /** DELETE /api/v1/system/logs?daysToKeep=: answers the number of entries deleted. */
    deleteLogs: (query: DeleteLogsQuery = {}) => http.delete<number>(`${BASE}/logs`, undefined, { query: { ...query } }),
    /** GET /api/v1/system/databases */
    databases: (signal?: AbortSignal) => http.get<DatabaseStatsDto[]>(`${BASE}/databases`, { signal }),
    /** GET /api/v1/system/databases/{channelId}/analysis */
    analyzeDatabase: (channelId: string, signal?: AbortSignal) =>
      http.get<PathAnalysisDto>(`${BASE}/databases/${enc(channelId)}/analysis`, { signal }),
    /** POST /api/v1/system/databases/{channelId}/repair: answers the number of entries repaired. */
    repairDatabase: (channelId: string, signal?: AbortSignal) =>
      http.post<number>(`${BASE}/databases/${enc(channelId)}/repair`, undefined, { signal }),
    /** POST /api/v1/system/maintenance/cleanup-tasks */
    cleanupTasks: () => http.post<void>(`${BASE}/maintenance/cleanup-tasks`),

    // ----- setup wizard (only while setup is incomplete; 409 `conflict` afterwards) -----
    /** POST /api/v1/system/setup/test-mongo: tests a connection string without saving it. */
    testMongo: (body: TestMongoRequest, signal?: AbortSignal) =>
      http.post<SetupTestResultDto>(`${BASE}/setup/test-mongo`, body, { signal }),
    /** POST /api/v1/system/setup/test-telegram: format check of the API id/hash. */
    testTelegram: (body: TestTelegramRequest, signal?: AbortSignal) =>
      http.post<SetupTestResultDto>(`${BASE}/setup/test-telegram`, body, { signal }),
    /** POST /api/v1/system/setup: tests, saves and applies either part; answers the new status. */
    saveSetup: (body: SaveSetupRequest, signal?: AbortSignal) => http.post<SetupStatusDto>(`${BASE}/setup`, body, { signal }),
    /** POST /api/v1/system/setup/complete (a 409 means it was already complete). */
    completeSetup: () => http.post<SetupStatusDto | null>(`${BASE}/setup/complete`),

    // ----- administration -----
    /** POST /api/v1/system/restart (202): the process stops about a second later. */
    restart: () => http.post<void>(`${BASE}/restart`),
    /** GET /api/v1/system/image-cache */
    imageCache: (signal?: AbortSignal) => http.get<ImageCacheInfoDto>(`${BASE}/image-cache`, { signal }),
    /** DELETE /api/v1/system/image-cache: answers the (now empty) cache info. */
    clearImageCache: () => http.delete<ImageCacheInfoDto>(`${BASE}/image-cache`),
    /** GET /api/v1/system/orphaned-favorites: favourite channels the account can no longer reach. */
    orphanedFavorites: (signal?: AbortSignal) => http.get<number[]>(`${BASE}/orphaned-favorites`, { signal }),
    /** POST /api/v1/system/orphaned-favorites: removes them; answers the ids removed. */
    cleanOrphanedFavorites: () => http.post<number[]>(`${BASE}/orphaned-favorites`),
    /** GET /api/v1/system/ffmpeg (cached 5 minutes; `refresh` probes again). */
    ffmpeg: (refresh = false, signal?: AbortSignal) =>
      http.get<FfmpegInfoDto>(`${BASE}/ffmpeg`, { query: { refresh: refresh || undefined }, signal }),
    /** GET /api/v1/system/version (GitHub check cached 30 minutes; `refresh` forces a new one). */
    version: (refresh = false, signal?: AbortSignal) =>
      http.get<VersionCheckDto>(`${BASE}/version`, { query: { refresh: refresh || undefined }, signal })
  };
}

export type SystemApi = ReturnType<typeof createSystemApi>;
