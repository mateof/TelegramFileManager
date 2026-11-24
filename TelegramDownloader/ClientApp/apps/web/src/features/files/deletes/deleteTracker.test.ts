import { ApiError, type ApiDeleteOperationDto } from '@tfm/api-client';
import type { FileEntry } from '@tfm/file-manager';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const hubHandlers: ((dto: ApiDeleteOperationDto) => void)[] = [];

vi.mock('@/lib/api', () => ({
  api: {
    system: { capabilities: vi.fn() },
    deleteOperations: { startChannel: vi.fn(), startLocal: vi.fn(), get: vi.fn(), cancel: vi.fn() },
    files: { delete: vi.fn() },
    local: { delete: vi.fn() }
  },
  errorMessage: (e: unknown) => (e instanceof Error ? e.message : String(e))
}));

vi.mock('@/lib/realtime', async () => {
  const { create } = await import('zustand');
  return {
    useRealtime: create(() => ({ eventsStatus: 'disconnected' })),
    getEventsHub: vi.fn(async () => ({
      on: (_name: string, handler: (dto: ApiDeleteOperationDto) => void) => {
        hubHandlers.push(handler);
        return () => undefined;
      }
    }))
  };
});

const { api } = await import('@/lib/api');
const { useRealtime } = await import('@/lib/realtime');
const { queryClient } = await import('@/lib/queryClient');
const { useDeletes, withoutPendingDeletes, percentOf } = await import('./deleteStore');
const tracker = await import('./deleteTracker');
const { deletePhaseText, deleteTitle } = await import('./deleteText');

const entry = (id: string, name = id): FileEntry => ({ id, name, kind: 'file', path: `/${name}`, parentPath: '/' }) as FileEntry;

function dto(patch: Partial<ApiDeleteOperationDto> = {}): ApiDeleteOperationDto {
  return {
    id: 'op1',
    kind: 'channel',
    channelId: '1001',
    channelName: 'Música',
    itemCount: 2,
    phase: 'deleting',
    counted: true,
    finished: false,
    totalFiles: 10,
    totalFolders: 1,
    totalBytes: 1000,
    processedFiles: 4,
    processedFolders: 0,
    processedBytes: 400,
    messagesDeleted: 4,
    indexOnly: false,
    currentItem: '/a/4.mp3',
    failedCount: 0,
    failed: [],
    cancelRequested: false,
    startedAt: new Date().toISOString(),
    ...patch
  };
}

const mocked = <T>(fn: T) => fn as unknown as ReturnType<typeof vi.fn>;

describe('delete tracker', () => {
  beforeEach(() => {
    tracker.__resetDeleteTrackerForTests();
    hubHandlers.length = 0;
    useRealtime.setState({ eventsStatus: 'disconnected' });
    mocked(api.system.capabilities).mockResolvedValue({ apiVersion: 1, serverVersion: 'x', features: ['files.deleteOperations'] });
    mocked(api.deleteOperations.startChannel).mockResolvedValue({ operationId: 'op1' });
    mocked(api.deleteOperations.startLocal).mockResolvedValue({ operationId: 'op1' });
  });
  afterEach(() => {
    tracker.__resetDeleteTrackerForTests();
    vi.useRealTimers();
  });

  const start = () =>
    tracker.startDelete({ kind: 'channel', channelId: '1001', placeName: 'Música', providerId: 'channel:1001', entries: [entry('a'), entry('b')] });

  it('starts an operation and hides the entries until it ends', async () => {
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    await start();

    expect(api.deleteOperations.startChannel).toHaveBeenCalledWith('1001', { ids: ['a', 'b'] });
    const op = useDeletes.getState().ops[0];
    expect(op).toMatchObject({ operationId: 'op1', status: 'running', mode: 'tracked' });
    expect(withoutPendingDeletes('channel:1001', [entry('a'), entry('c')]).map((e) => e.id)).toEqual(['c']);
    expect(deleteTitle(op)).toBe('Deleting 2 items from Música');

    tracker.applyProgress(dto({ phase: 'done', finished: true, processedFiles: 10, processedFolders: 1, processedBytes: 1000 }));

    expect(useDeletes.getState().ops[0].status).toBe('done');
    expect(withoutPendingDeletes('channel:1001', [entry('a')]).map((e) => e.id)).toEqual(['a']);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['tfm-file-manager', 'channel:1001'] });
    expect(deleteTitle(useDeletes.getState().ops[0])).toBe('Deleted 2 items from Música');
  });

  it('polls every second while the events hub is down', async () => {
    vi.useFakeTimers();
    mocked(api.deleteOperations.get).mockResolvedValue(dto());
    await start();

    await vi.advanceTimersByTimeAsync(1000);
    expect(api.deleteOperations.get).toHaveBeenCalledWith('op1');
    expect(percentOf(useDeletes.getState().ops[0])).toBe(40);
    expect(deletePhaseText(useDeletes.getState().ops[0])).toBe('Deleting...');

    mocked(api.deleteOperations.get).mockResolvedValue(dto({ phase: 'done', finished: true, failedCount: 1, failed: [{ path: '/b', reason: 'Telegram: x' }] }));
    await vi.advanceTimersByTimeAsync(1000);
    expect(useDeletes.getState().ops[0]).toMatchObject({ status: 'partial', failedCount: 1 });

    const calls = mocked(api.deleteOperations.get).mock.calls.length;
    await vi.advanceTimersByTimeAsync(3000);
    expect(mocked(api.deleteOperations.get).mock.calls.length).toBe(calls); // stops once finished
  });

  it('uses the hub messages and only polls when they stop arriving', async () => {
    vi.useFakeTimers();
    useRealtime.setState({ eventsStatus: 'connected' });
    mocked(api.deleteOperations.get).mockResolvedValue(dto({ processedFiles: 9 }));
    await start();
    await vi.advanceTimersByTimeAsync(0);
    expect(hubHandlers).toHaveLength(1);

    hubHandlers[0](dto({ phase: 'waiting', waitUntil: new Date(Date.now() + 30_000).toISOString() }));
    expect(deletePhaseText(useDeletes.getState().ops[0])).toBe('Waiting for Telegram (flood wait) 30 s');
    await vi.advanceTimersByTimeAsync(3000);
    expect(api.deleteOperations.get).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(3000); // no news for 5 s: safety poll
    expect(api.deleteOperations.get).toHaveBeenCalled();
  });

  it('cancels through the API', async () => {
    mocked(api.deleteOperations.cancel).mockResolvedValue(dto({ cancelRequested: true }));
    await start();
    const key = useDeletes.getState().ops[0].key;

    await tracker.cancelDelete(key);
    expect(api.deleteOperations.cancel).toHaveBeenCalledWith('op1');

    tracker.applyProgress(dto({ phase: 'cancelled', finished: true }));
    expect(useDeletes.getState().ops[0].status).toBe('cancelled');
  });

  it('falls back to the synchronous endpoint on servers without the capability', async () => {
    mocked(api.system.capabilities).mockRejectedValue(new ApiError('not_found', 'no', 404));
    mocked(api.local.delete).mockResolvedValue({ accepted: 1, skipped: ['music/b.mp3'], taskId: null });

    await tracker.startDelete({ kind: 'local', placeName: 'Local files', providerId: 'local', entries: [entry('music/a.mp3', 'a.mp3'), entry('music/b.mp3', 'b.mp3')] });
    await vi.waitFor(() => expect(useDeletes.getState().ops[0].status).toBe('partial'));

    expect(api.deleteOperations.startLocal).not.toHaveBeenCalled();
    expect(api.local.delete).toHaveBeenCalledWith({ paths: ['music/a.mp3', 'music/b.mp3'] });
    expect(useDeletes.getState().ops[0]).toMatchObject({ mode: 'sync', failedCount: 1, failures: [{ path: '/b.mp3', reason: '' }] });
  });

  it('restores the entries and rethrows when the operation cannot start', async () => {
    mocked(api.deleteOperations.startChannel).mockRejectedValue(new ApiError('invalid_request', 'bad', 400));
    await expect(start()).rejects.toThrow('bad');
    expect(useDeletes.getState().ops).toEqual([]);
    expect(useDeletes.getState().hidden).toEqual({});
  });

  it('reports an operation the server forgot (404 while polling) as failed', async () => {
    vi.useFakeTimers();
    mocked(api.deleteOperations.get).mockRejectedValue(new ApiError('not_found', 'gone', 404));
    await start();
    await vi.advanceTimersByTimeAsync(1000);
    expect(useDeletes.getState().ops[0].status).toBe('failed');
  });
});
