import type { ApiDeleteFailureDto, ApiDeleteOperationDto } from '@tfm/api-client';
import { create } from 'zustand';

/**
 * State of the deletes started from the file managers (channels and local
 * files), shown by <DeleteProgressHost/> on every page. This module is in the
 * initial bundle (the host and the top bar read it), so it only holds state
 * and pure helpers: the logic that talks to the server is `deleteTracker.ts`.
 */

export type DeleteStatus = 'starting' | 'running' | 'done' | 'partial' | 'failed' | 'cancelled';

export interface TrackedDelete {
  /** Local id (the server id is `operationId`). */
  key: string;
  kind: 'channel' | 'local';
  channelId?: string;
  /** Channel name, or the local files root name. */
  placeName: string;
  itemCount: number;
  /** Name of the item when only one was selected. */
  firstName?: string;
  /** File manager provider whose listings show the entries. */
  providerId: string;
  entryIds: string[];
  /** `tracked`: background operation with progress; `sync`: old server, indeterminate. */
  mode: 'tracked' | 'sync';
  operationId?: string;
  progress?: ApiDeleteOperationDto;
  status: DeleteStatus;
  error?: string;
  failures: ApiDeleteFailureDto[];
  failedCount: number;
  cancelling?: boolean;
  /** `Date.now()` of the last news from the server (hub or poll). */
  lastUpdate: number;
  startedAt: number;
}

interface DeleteState {
  ops: TrackedDelete[];
  /** Entry ids hidden from the listings of each provider while their delete runs. */
  hidden: Record<string, string[]>;
  add: (op: TrackedDelete) => void;
  update: (key: string, patch: Partial<TrackedDelete>) => void;
  remove: (key: string) => void;
  hide: (providerId: string, ids: string[]) => void;
  unhide: (providerId: string, ids: string[]) => void;
}

export const useDeletes = create<DeleteState>()((set) => ({
  ops: [],
  hidden: {},
  add: (op) => set((s) => ({ ops: [...s.ops, op] })),
  update: (key, patch) => set((s) => ({ ops: s.ops.map((o) => (o.key === key ? { ...o, ...patch } : o)) })),
  remove: (key) => set((s) => ({ ops: s.ops.filter((o) => o.key !== key) })),
  hide: (providerId, ids) =>
    set((s) => ({ hidden: { ...s.hidden, [providerId]: [...new Set([...(s.hidden[providerId] ?? []), ...ids])] } })),
  unhide: (providerId, ids) =>
    set((s) => {
      const drop = new Set(ids);
      const left = (s.hidden[providerId] ?? []).filter((id) => !drop.has(id));
      const hidden = { ...s.hidden };
      if (left.length) hidden[providerId] = left;
      else delete hidden[providerId];
      return { hidden };
    })
}));

export function isActiveDelete(op: Pick<TrackedDelete, 'status'>): boolean {
  return op.status === 'starting' || op.status === 'running';
}

/** Status of a tracked delete from the server state. */
export function statusFromProgress(dto: ApiDeleteOperationDto): DeleteStatus {
  switch (dto.phase) {
    case 'done':
      return dto.failedCount > 0 ? 'partial' : 'done';
    case 'failed':
      return 'failed';
    case 'cancelled':
      return 'cancelled';
    default:
      return 'running';
  }
}

/** 0-100 once the totals are known; undefined = indeterminate. A cancelled delete keeps how far it got. */
export function percentOf(op: Pick<TrackedDelete, 'progress' | 'status'>): number | undefined {
  if (op.status === 'done') return 100;
  const p = op.progress;
  if (!p?.counted) return isActiveDelete(op) ? undefined : 100;
  // Bytes are the honest measure when files dominate; items for folders-only or empty files
  if (p.totalBytes > 0) return Math.min(100, (p.processedBytes / p.totalBytes) * 100);
  const total = p.totalFiles + p.totalFolders;
  return total > 0 ? Math.min(100, ((p.processedFiles + p.processedFolders) / total) * 100) : 0;
}

/** Seconds left of a Telegram FLOOD_WAIT, or null when not waiting. */
export function waitSecondsLeft(dto: ApiDeleteOperationDto | undefined, now = Date.now()): number | null {
  if (dto?.phase !== 'waiting' || !dto.waitUntil) return null;
  return Math.max(0, Math.ceil((Date.parse(dto.waitUntil) - now) / 1000));
}

/** Removes the entries whose delete is running (the listings stay consistent while refetching). */
export function withoutPendingDeletes<T extends { id: string }>(providerId: string, items: T[]): T[] {
  const hidden = useDeletes.getState().hidden[providerId];
  if (!hidden?.length) return items;
  const set = new Set(hidden);
  return items.filter((e) => !set.has(e.id));
}
