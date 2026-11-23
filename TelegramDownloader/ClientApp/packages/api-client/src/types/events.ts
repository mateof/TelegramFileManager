// Payloads of the /hubs/events SignalR hub (application-wide realtime events).
// PROVISIONAL where marked: the hub is written by the API agent in parallel; align at merge.
import type { ApiChannelRefreshProgressDto, ApiChannelRefreshResultDto } from './channels';
import type { ApiLibraryScanStateDto } from './library';
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
  kind: string;
  percent: number;
  message: string;
  done: boolean;
  error?: string | null;
}

/** `AuthState`: the Telegram session logged in or out. */
export interface AuthStateEvent {
  telegramLoggedIn: boolean;
}

/** `Metrics`: a `SystemMetricsDto` every 2 seconds, only after invoking `SubscribeMetrics`. */
export type MetricsEvent = SystemMetricsDto;

/** `LibraryScan`: every second while a library scan runs, once when it ends (`running: false`). */
export interface LibraryScanEvent {
  running: boolean;
  /** Same payload as `GET /api/v1/library/scan`. */
  progress?: ApiLibraryScanStateDto | null;
}

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
