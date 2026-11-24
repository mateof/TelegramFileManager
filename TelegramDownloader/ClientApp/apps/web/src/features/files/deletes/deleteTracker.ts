import { isApiError, isFinalDeletePhase, type ApiDeleteOperationDto, type TransferAcceptedDto } from '@tfm/api-client';
import { fileManagerQueryKey, type FileEntry, type Page } from '@tfm/file-manager';
import type { InfiniteData } from '@tanstack/react-query';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryClient } from '@/lib/queryClient';
import { queryKeys } from '@/lib/queryKeys';
import { getEventsHub, useRealtime } from '@/lib/realtime';
import { isActiveDelete, statusFromProgress, useDeletes, type TrackedDelete } from './deleteStore';

/**
 * Starts and follows the deletes of the file managers.
 *
 * With a server that advertises `files.deleteOperations` the delete runs in
 * the background: progress arrives as `DeleteProgress` on /hubs/events, and
 * a 1 s poll of `GET /api/v1/delete-operations/{id}` covers a hub that is
 * down (or a message it missed). Older servers get the synchronous endpoint
 * with an indeterminate progress panel.
 *
 * The entries disappear from the listings at once and the listings are
 * reloaded when the delete ends, so the entries that failed come back.
 */

export const DELETE_OPERATIONS_FEATURE = 'files.deleteOperations';

/** Poll period, and how old the last news may be before polling: hub down / hub up. */
export const POLL_INTERVAL_MS = 1000;
const STALE_WITHOUT_HUB_MS = 900;
const STALE_WITH_HUB_MS = 5000;
/** A successful delete leaves the panel after this long. */
export const AUTO_DISMISS_MS = 6000;

export interface StartDeleteRequest {
  kind: 'channel' | 'local';
  /** Channel id (channel deletes). */
  channelId?: string;
  /** Channel name / local root name, for the title. */
  placeName: string;
  providerId: string;
  entries: FileEntry[];
}

let capabilities: Promise<Set<string>> | null = null;

/** Optional features of the server (an old server answers 404: none). Cached for the session. */
export function serverFeatures(): Promise<Set<string>> {
  capabilities ??= api.system
    .capabilities()
    .then((c) => new Set(c?.features ?? []))
    .catch(() => {
      capabilities = null; // try again next time (it may have been a network blip)
      return new Set<string>();
    });
  return capabilities;
}

let nextKey = 1;

export async function startDelete(request: StartDeleteRequest): Promise<void> {
  const { entries, providerId } = request;
  if (entries.length === 0) return;
  const ids = entries.map((e) => e.id);
  const key = `delete-${nextKey++}`;
  const now = Date.now();
  const store = useDeletes.getState();
  store.add({
    key,
    kind: request.kind,
    channelId: request.channelId,
    placeName: request.placeName,
    itemCount: entries.length,
    firstName: entries.length === 1 ? entries[0].name : undefined,
    providerId,
    entryIds: ids,
    mode: 'tracked',
    status: 'starting',
    failures: [],
    failedCount: 0,
    lastUpdate: now,
    startedAt: now
  });
  hideEntries(providerId, ids);

  const features = await serverFeatures();
  if (!features.has(DELETE_OPERATIONS_FEATURE)) {
    void runSync(key, request);
    return;
  }

  try {
    const started =
      request.kind === 'channel'
        ? await api.deleteOperations.startChannel(request.channelId ?? '', { ids })
        : await api.deleteOperations.startLocal({ paths: ids });
    useDeletes.getState().update(key, { operationId: started.operationId, status: 'running', lastUpdate: Date.now() });
    listenToHub();
    ensurePolling();
  } catch (error) {
    if (isApiError(error) && error.status === 404) {
      // Advertised but missing (proxy, partial deploy): the old endpoint still works
      void runSync(key, request);
      return;
    }
    useDeletes.getState().remove(key);
    useDeletes.getState().unhide(providerId, ids);
    throw error;
  }
}

/** Old servers: the synchronous endpoint, with an indeterminate panel. */
async function runSync(key: string, request: StartDeleteRequest): Promise<void> {
  const { update } = useDeletes.getState();
  update(key, { mode: 'sync', status: 'running' });
  const ids = request.entries.map((e) => e.id);
  try {
    const res: TransferAcceptedDto =
      request.kind === 'channel'
        ? await api.files.delete(request.channelId ?? '', { ids })
        : await api.local.delete({ paths: ids });
    const skipped = res?.skipped ?? [];
    const names = new Map(request.entries.map((e) => [e.id, e.path || e.name]));
    update(key, {
      status: skipped.length ? 'partial' : 'done',
      failedCount: skipped.length,
      failures: skipped.map((id) => ({ path: names.get(id) ?? id, reason: '' }))
    });
  } catch (error) {
    update(key, { status: 'failed', error: errorMessage(error) });
  }
  finish(key);
}

/** Applies a server state (hub message, poll or cancel answer) to the delete it belongs to. */
export function applyProgress(dto: ApiDeleteOperationDto): void {
  const op = useDeletes.getState().ops.find((o) => o.operationId === dto.id);
  if (!op || !isActiveDelete(op)) return;
  // A late poll answer must not move the counters backwards
  if (op.progress && !dto.finished && dto.processedFiles + dto.processedFolders < op.progress.processedFiles + op.progress.processedFolders) return;
  useDeletes.getState().update(op.key, {
    progress: dto,
    status: statusFromProgress(dto),
    failures: dto.failed,
    failedCount: dto.failedCount,
    error: dto.error ?? undefined,
    cancelling: op.cancelling && !isFinalDeletePhase(dto.phase),
    lastUpdate: Date.now()
  });
  if (isFinalDeletePhase(dto.phase)) finish(op.key);
}

const dismissTimers = new Map<string, ReturnType<typeof setTimeout>>();

/** The delete ended: show the entries that are left and reload what depends on them. */
function finish(key: string): void {
  const op = useDeletes.getState().ops.find((o) => o.key === key);
  if (!op) return;
  useDeletes.getState().unhide(op.providerId, op.entryIds);
  void queryClient.invalidateQueries({ queryKey: fileManagerQueryKey(op.providerId) });
  if (op.kind === 'channel' && op.channelId) {
    void queryClient.invalidateQueries({ queryKey: queryKeys.channel(op.channelId) });
  }
  if (op.status === 'done') {
    dismissTimers.set(
      key,
      setTimeout(() => dismissDelete(key), AUTO_DISMISS_MS)
    );
  }
}

/** Asks the server to stop a running delete (what is deleted stays deleted). */
export async function cancelDelete(key: string): Promise<void> {
  const op = useDeletes.getState().ops.find((o) => o.key === key);
  if (!op?.operationId || !isActiveDelete(op) || op.cancelling) return;
  useDeletes.getState().update(key, { cancelling: true });
  try {
    const dto = await api.deleteOperations.cancel(op.operationId);
    if (dto) applyProgress(dto);
  } catch (error) {
    // 409: it finished meanwhile; the next poll brings the final state
    if (!(isApiError(error) && error.status === 409)) useDeletes.getState().update(key, { cancelling: false });
  }
  useDeletes.getState().update(key, { lastUpdate: 0 }); // poll right away
}

/** Removes a finished delete from the panel. */
export function dismissDelete(key: string): void {
  const timer = dismissTimers.get(key);
  if (timer) clearTimeout(timer);
  dismissTimers.delete(key);
  useDeletes.getState().remove(key);
}

// ---- listings ------------------------------------------------------------

function hideEntries(providerId: string, ids: string[]): void {
  useDeletes.getState().hide(providerId, ids);
  const drop = new Set(ids);
  queryClient.setQueriesData<InfiniteData<Page<FileEntry>>>({ queryKey: fileManagerQueryKey(providerId) }, (data) => {
    if (!data?.pages) return data;
    return {
      ...data,
      pages: data.pages.map((page) => {
        const items = page.items.filter((e) => !drop.has(e.id));
        const removed = page.items.length - items.length;
        return removed ? { ...page, items, totalItems: Math.max(0, (page.totalItems ?? items.length) - removed) } : page;
      })
    };
  });
}

// ---- realtime + polling ----------------------------------------------------

let hubListening = false;

function listenToHub(): void {
  if (hubListening) return;
  hubListening = true;
  void getEventsHub()
    .then((hub) => hub.on('DeleteProgress', (dto) => applyProgress(dto)))
    .catch(() => {
      hubListening = false;
    });
}

let pollTimer: ReturnType<typeof setInterval> | null = null;
const inFlight = new Set<string>();

function ensurePolling(): void {
  if (pollTimer) return;
  pollTimer = setInterval(() => void pollOnce(), POLL_INTERVAL_MS);
}

/** One poll round: every tracked delete without recent news is read from the server. */
export async function pollOnce(now = Date.now()): Promise<void> {
  const active = useDeletes.getState().ops.filter((o) => o.mode === 'tracked' && o.operationId && isActiveDelete(o));
  if (active.length === 0) {
    if (pollTimer) clearInterval(pollTimer);
    pollTimer = null;
    return;
  }
  const hubUp = useRealtime.getState().eventsStatus === 'connected';
  const staleAfter = hubUp ? STALE_WITH_HUB_MS : STALE_WITHOUT_HUB_MS;
  await Promise.all(
    active
      .filter((o) => now - o.lastUpdate >= staleAfter && !inFlight.has(o.key))
      .map(async (o) => {
        inFlight.add(o.key);
        try {
          applyProgress(await api.deleteOperations.get(o.operationId!));
        } catch (error) {
          if (isApiError(error) && error.status === 404) {
            useDeletes.getState().update(o.key, { status: 'failed', error: t('The server no longer knows this delete') });
            finish(o.key);
          }
          // Other errors (network): try again on the next round
        } finally {
          inFlight.delete(o.key);
        }
      })
  );
}

/** Test helper: forget the module state. */
export function __resetDeleteTrackerForTests(): void {
  capabilities = null;
  hubListening = false;
  if (pollTimer) clearInterval(pollTimer);
  pollTimer = null;
  inFlight.clear();
  dismissTimers.forEach((t) => clearTimeout(t));
  dismissTimers.clear();
  useDeletes.setState({ ops: [], hidden: {} });
}

export type { TrackedDelete };
