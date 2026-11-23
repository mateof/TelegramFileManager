import type { HttpClient, Paged } from '../http';
import type {
  DatabaseStatsDto,
  DeleteLogsQuery,
  LogEntryDto,
  LogQuery,
  PathAnalysisDto,
  ServerInfoDto,
  SetupStatusDto,
  SystemMetricsDto
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
    cleanupTasks: () => http.post<void>(`${BASE}/maintenance/cleanup-tasks`)
  };
}

export type SystemApi = ReturnType<typeof createSystemApi>;
