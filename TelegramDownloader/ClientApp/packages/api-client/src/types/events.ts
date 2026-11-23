// Payloads of the /hubs/events SignalR hub (application-wide realtime events).
// PROVISIONAL where marked: the hub is written by the API agent in parallel; align at merge.
import type { ApiChannelRefreshProgressDto, ApiChannelRefreshResultDto } from './channels';

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

/** `Metrics`: system metrics, pushed after invoking `SubscribeMetrics`. PROVISIONAL shape. */
export type MetricsEvent = Record<string, unknown>;

/** `LibraryScan`: progress/result of a media library scan. PROVISIONAL shape. */
export type LibraryScanEvent = Record<string, unknown>;

/** Message name → payload of every server → client message of /hubs/events. */
export interface EventsHubMessages {
  Notification: EventNotification;
  ChannelRefreshed: ChannelRefreshedEvent;
  ChannelRefreshProgress: ChannelRefreshProgressEvent;
  ImportProgress: ImportProgressEvent;
  AuthState: AuthStateEvent;
  Metrics: MetricsEvent;
  LibraryScan: LibraryScanEvent;
}
