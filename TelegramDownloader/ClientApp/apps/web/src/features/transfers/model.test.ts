import type { TransferDto, TransfersSnapshotDto } from '@tfm/api-client';
import { describe, expect, it } from 'vitest';
import { appendSample, availableActions, formatEta, historyToSeries, matchesFilter, SpeedTracker, stateGroup, tabCounts, tabItems } from './model';

let n = 0;
function transfer(over: Partial<TransferDto> = {}): TransferDto {
  n++;
  return {
    id: `t${n}`,
    kind: 'download',
    action: 'Download',
    state: 'Working',
    isQueued: false,
    name: `file${n}.bin`,
    path: '/downloads',
    channelId: '1',
    channelName: 'Channel',
    size: 1000,
    transmitted: 0,
    sizeText: '1 KB',
    transmittedText: '0 B',
    progress: 0,
    createdAt: `2026-01-01T00:00:0${n % 10}Z`,
    startedAt: null,
    endedAt: null,
    totalItems: null,
    executedItems: null,
    isUpload: null,
    fromPath: null,
    toPath: null,
    ...over
  };
}

const summary = {
  activeDownloads: 0,
  queuedDownloads: 0,
  activeUploads: 0,
  queuedUploads: 0,
  activeTasks: 0,
  totalTasks: 0,
  downloadSpeed: '0',
  uploadSpeed: '0',
  downloadBytesPerSecond: 0,
  uploadBytesPerSecond: 0,
  downloadsPaused: false,
  isWorking: false
};

describe('transfers model', () => {
  it('groups states', () => {
    expect(stateGroup(transfer({ state: 'Working' }))).toBe('active');
    expect(stateGroup(transfer({ state: 'Pending', isQueued: true }))).toBe('queued');
    expect(stateGroup(transfer({ state: 'Paused' }))).toBe('paused');
    expect(stateGroup(transfer({ state: 'Completed' }))).toBe('done');
    expect(stateGroup(transfer({ state: 'Error' }))).toBe('failed');
    expect(stateGroup(transfer({ state: 'Canceled' }))).toBe('canceled');
    expect(matchesFilter(transfer({ state: 'Paused' }), 'queued')).toBe(true);
    expect(matchesFilter(transfer({ state: 'Canceled' }), 'finished')).toBe(true);
    expect(matchesFilter(transfer({ state: 'Error' }), 'failed')).toBe(true);
    expect(matchesFilter(transfer({ state: 'Error' }), 'active')).toBe(false);
  });

  it('builds tabs: running first, then the queue in order, then finished (newest first), no duplicates', () => {
    const done1 = transfer({ state: 'Completed', endedAt: '2026-01-01T01:00:00Z' });
    const done2 = transfer({ state: 'Completed', endedAt: '2026-01-01T02:00:00Z' });
    const running = transfer();
    const q1 = transfer({ state: 'Pending', isQueued: true });
    const q2 = transfer({ state: 'Pending', isQueued: true });
    const snapshot: TransfersSnapshotDto = {
      downloads: [done1, running, done2],
      queuedDownloads: [q1, q2, running],
      uploads: [transfer({ kind: 'upload', state: 'Error' })],
      queuedUploads: [],
      tasks: [transfer({ kind: 'task' })],
      summary
    };
    expect(tabItems(snapshot, 'downloads').map((t) => t.id)).toEqual([running.id, q1.id, q2.id, done2.id, done1.id]);
    const counts = tabCounts(snapshot);
    expect(counts.downloads).toEqual({ total: 5, active: 1, failed: 0 });
    expect(counts.uploads).toEqual({ total: 1, active: 0, failed: 1 });
    expect(counts.tasks.active).toBe(1);
    expect(tabItems(null, 'tasks')).toEqual([]);
  });

  it('offers the right actions per state and kind', () => {
    expect(availableActions(transfer())).toEqual(['pause', 'cancel']);
    expect(availableActions(transfer({ kind: 'upload' }))).toEqual(['cancel']);
    expect(availableActions(transfer({ state: 'Paused' }))).toEqual(['resume', 'cancel', 'remove']);
    expect(availableActions(transfer({ state: 'Pending', isQueued: true }))).toEqual(['cancel', 'remove']);
    expect(availableActions(transfer({ state: 'Error' }))).toEqual(['retry', 'remove']);
    expect(availableActions(transfer({ kind: 'upload', state: 'Error' }))).toEqual(['remove']);
    expect(availableActions(transfer({ kind: 'task', state: 'Canceled' }))).toEqual(['retry', 'remove']);
    expect(availableActions(transfer({ state: 'Completed' }))).toEqual(['remove']);
  });

  it('follows what the server says can be paused and retried', () => {
    expect(availableActions(transfer({ kind: 'upload', canPause: true, canRetry: false }))).toEqual(['pause', 'cancel']);
    expect(availableActions(transfer({ kind: 'upload', state: 'Paused', canPause: true, canRetry: false }))).toEqual(['resume', 'cancel']);
    // Tied to one HTTP request: it cannot be started again
    expect(availableActions(transfer({ canPause: false, canRetry: false }))).toEqual(['cancel']);
    expect(availableActions(transfer({ state: 'Canceled', canPause: false, canRetry: false }))).toEqual(['remove']);
  });

  it('computes a smoothed speed and the ETA from consecutive snapshots', () => {
    const tracker = new SpeedTracker(0.5);
    const item = transfer({ size: 10_000, transmitted: 0 });
    tracker.update([item], 0);
    expect(tracker.speed(item.id)).toBe(0);
    tracker.update([{ ...item, transmitted: 1000 }], 1000);
    expect(tracker.speed(item.id)).toBe(1000);
    tracker.update([{ ...item, transmitted: 4000 }], 2000);
    expect(tracker.speed(item.id)).toBe(2000); // 1000 + 0.5 * (3000 - 1000)
    expect(tracker.eta({ ...item, transmitted: 4000 })).toBe(3);
    // Finished or gone: no ETA, forgotten
    tracker.update([{ ...item, state: 'Completed', transmitted: 10_000 }], 3000);
    expect(tracker.eta({ ...item, state: 'Completed' })).toBeNull();
    tracker.update([], 4000);
    expect(tracker.speed(item.id)).toBe(0);
  });

  it('formats ETAs', () => {
    expect(formatEta(null)).toBe('');
    expect(formatEta(9)).toBe('9 s');
    expect(formatEta(75)).toBe('1 min 15 s');
    expect(formatEta(3725)).toBe('1 h 2 min');
    expect(formatEta(200_000)).toBe('2 d 7 h');
  });

  it('merges the speed history and keeps a rolling window', () => {
    const p = (s: number, bps: number) => ({ time: new Date(s * 1000).toISOString(), bytesPerSecond: bps, speedText: '', activeFiles: [] });
    const series = historyToSeries({ download: [p(10, 5), p(13, 6)], upload: [p(13, 2)], intervalSeconds: 3, windowSeconds: 600 });
    expect(series).toEqual([
      { t: 10_000, down: 5, up: 0 },
      { t: 13_000, down: 6, up: 2 }
    ]);
    const next = appendSample(series, p(20, 7), p(20, 1), 8);
    expect(next.map((s) => s.t)).toEqual([13_000, 20_000]);
    // A sample with the same timestamp replaces the last one
    expect(appendSample(next, p(20, 9), p(20, 0), 600).at(-1)).toEqual({ t: 20_000, down: 9, up: 0 });
  });
});
