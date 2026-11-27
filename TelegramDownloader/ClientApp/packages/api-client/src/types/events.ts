// Payloads of the /hubs/events SignalR hub (application-wide realtime events).
// PROVISIONAL where marked: the hub is written by the API agent in parallel; align at merge.
import type { ApiChannelRefreshProgressDto, ApiChannelRefreshResultDto } from './channels';
import type { ApiDeleteOperationDto } from './deleteOperations';
import type { ApiLibraryScanStateDto } from './library';
import type { PlayerDataChangedEvent } from './player';
import type { SystemMetricsDto } from './system';

export type NotificationLevel = 'info' | 'success' | 'warning' | 'error';

/** `Notification`: a toast-worthy message raised anywhere on the server. */
export interface EventNotification {
  level: NotificationLevel | (string & {});
  title?: string | null;
  message: string;
  /** ISO timestamp. */
  at: string;
}

/** `ChannelRefreshed`: an index refresh finished (or failed / was cancelled). */
export interface ChannelRefreshedEvent {
  channelId: string;
  result: ApiChannelRefreshResultDto;
}

/** `ChannelRefreshProgress`: live progress of a running refresh. */
export interface ChannelRefreshProgressEvent {
  channelId: string;
  progress: ApiChannelRefreshProgressDto;
}

/** `ImportProgress`: progress of an import/export/maintenance job. */
export interface ImportProgressEvent {
  id: string;
  /** `index` (channel index import) or `share` (shared collection import). */
  kind: 'index' | 'share' | (string & {});
  percent: number;
  message: string;
  /** Items to import and imported so far. */
  total?: number;
  completed?: number;
  done: boolean;
  error?: string | null;
}

/** Answer of the endpoints that start an import (`POST .../files/import`, `POST /api/v1/shares/import`). */
export interface ImportStartedDto {
  /** Correlates the `ImportProgress` messages. */
  importId: string;
  kind: 'index' | 'share' | (string & {});
}

/** `AuthState`: the Telegram session logged in or out. */
export interface AuthStateEvent {
  telegramLoggedIn: boolean;
}

/** `Metrics`: a `SystemMetricsDto` every 2 seconds, only after invoking `SubscribeMetrics`. */
export type MetricsEvent = SystemMetricsDto;

/**
 * `LibraryScan`: every second while a media library scan runs, once when it ends (`running: false`)
 * and on connect when one is running.
 */
export interface LibraryScanEvent {
  running: boolean;
  /** Same shape as `GET /api/v1/library/scan`. */
  progress?: ApiLibraryScanStateDto | null;
}

/**
 * `DeleteProgress`: a background delete (`POST .../files/delete-operations`, `POST /api/v1/local/delete-operations`).
 * At most 4 per second per operation, always the final state (`finished: true`), and on connect for each running one.
 */
export type DeleteProgressEvent = ApiDeleteOperationDto;

/** Message name → payload of every server → client message of /hubs/events. */
export interface EventsHubMessages {
  Notification: EventNotification;
  ChannelRefreshed: ChannelRefreshedEvent;
  ChannelRefreshProgress: ChannelRefreshProgressEvent;
  ImportProgress: ImportProgressEvent;
  AuthState: AuthStateEvent;
  Metrics: MetricsEvent;
  LibraryScan: LibraryScanEvent;
  DeleteProgress: DeleteProgressEvent;
  /** Favourites, history or resume positions changed (from any device). */
  PlayerDataChanged: PlayerDataChangedEvent;
}
