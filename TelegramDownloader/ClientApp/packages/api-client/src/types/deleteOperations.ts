// Background deletes with progress (DeleteOperationsController, Models/Api/DeleteOperationDtos.cs).

/** What a delete operation removes. */
export const DeleteKind = {
  /** Entries of a channel index (and their Telegram messages). */
  Channel: 'channel',
  /** Files and folders of the server's local storage. */
  Local: 'local'
} as const;

export type DeleteKind = (typeof DeleteKind)[keyof typeof DeleteKind];

/** `counting` → `deleting` (`waiting` during a Telegram FLOOD_WAIT) → `done` | `failed` | `cancelled`. */
export const DeletePhase = {
  Counting: 'counting',
  Deleting: 'deleting',
  Waiting: 'waiting',
  Done: 'done',
  Failed: 'failed',
  Cancelled: 'cancelled'
} as const;

export type DeletePhase = (typeof DeletePhase)[keyof typeof DeletePhase];

/** Phases after which nothing changes. */
export function isFinalDeletePhase(phase: string): boolean {
  return phase === DeletePhase.Done || phase === DeletePhase.Failed || phase === DeletePhase.Cancelled;
}

/** An item a delete operation could not remove. */
export interface ApiDeleteFailureDto {
  /** Channel path (`/music/a.mp3`) or local path relative to the root. */
  path: string;
  reason: string;
}

/** `GET /api/v1/delete-operations/{id}` and the `DeleteProgress` message of /hubs/events. */
export interface ApiDeleteOperationDto {
  id: string;
  kind: DeleteKind | (string & {});
  channelId?: string | null;
  channelName?: string | null;
  /** Items the client selected. */
  itemCount: number;
  phase: DeletePhase | (string & {});
  /** The totals are known (after `counting`). */
  counted: boolean;
  finished: boolean;
  totalFiles: number;
  totalFolders: number;
  totalBytes: number;
  processedFiles: number;
  processedFolders: number;
  processedBytes: number;
  /** Telegram messages deleted so far. */
  messagesDeleted: number;
  /** Channel where the account cannot delete messages: only the index entries go. */
  indexOnly: boolean;
  currentItem?: string | null;
  /** May exceed `failed.length` (capped at 100). */
  failedCount: number;
  failed: ApiDeleteFailureDto[];
  /** While `waiting`: when Telegram allows the next call (ISO, UTC). */
  waitUntil?: string | null;
  cancelRequested: boolean;
  /** Why the operation failed (`failed`). */
  error?: string | null;
  startedAt: string;
  finishedAt?: string | null;
}

/** Answer of `POST .../files/delete-operations` and `POST /api/v1/local/delete-operations`. */
export interface DeleteOperationStartedDto {
  operationId: string;
}
