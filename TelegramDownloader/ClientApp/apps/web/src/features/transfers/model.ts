import type { SpeedHistoryDto, SpeedPointDto, TransferDto, TransfersSnapshotDto } from '@tfm/api-client';

/**
 * Pure view-model of the transfers page: how the hub snapshot becomes tabs,
 * which actions each item allows, per-item speed/ETA (the server only reports
 * bytes), and the rolling speed history of the chart.
 */

export type TransferTab = 'downloads' | 'uploads' | 'tasks';
export type StateGroup = 'active' | 'queued' | 'paused' | 'done' | 'failed' | 'canceled';
export type TransferAction = 'pause' | 'resume' | 'cancel' | 'retry' | 'remove';
export type StateFilter = 'all' | 'active' | 'queued' | 'finished' | 'failed';

/** Bucket of a transfer state (`StateTask` names). */
export function stateGroup(t: Pick<TransferDto, 'state' | 'isQueued'>): StateGroup {
  switch (t.state) {
    case 'Working':
      return 'active';
    case 'Paused':
      return 'paused';
    case 'Completed':
      return 'done';
    case 'Error':
      return 'failed';
    case 'Canceled':
      return 'canceled';
    default:
      return 'queued'; // Pending (queued or about to start)
  }
}

export function matchesFilter(t: TransferDto, filter: StateFilter): boolean {
  if (filter === 'all') return true;
  const g = stateGroup(t);
  if (filter === 'active') return g === 'active';
  if (filter === 'queued') return g === 'queued' || g === 'paused';
  if (filter === 'finished') return g === 'done' || g === 'canceled';
  return g === 'failed';
}

/** Items of a tab: running/finished first, then the queue (no duplicates). */
export function tabItems(snapshot: TransfersSnapshotDto | null, tab: TransferTab): TransferDto[] {
  if (!snapshot) return [];
  const lists =
    tab === 'downloads' ? [snapshot.downloads, snapshot.queuedDownloads] : tab === 'uploads' ? [snapshot.uploads, snapshot.queuedUploads] : [snapshot.tasks];
  const seen = new Set<string>();
  const out: TransferDto[] = [];
  for (const list of lists) {
    for (const item of list ?? []) {
      if (seen.has(item.id)) continue;
      seen.add(item.id);
      out.push(item);
    }
  }
  // Working first, then queued/paused, then finished (most recent first)
  const rank: Record<StateGroup, number> = { active: 0, queued: 1, paused: 1, failed: 2, canceled: 3, done: 3 };
  return out
    .map((item, i) => ({ item, i }))
    .sort((a, b) => {
      const r = rank[stateGroup(a.item)] - rank[stateGroup(b.item)];
      if (r !== 0) return r;
      if (rank[stateGroup(a.item)] >= 2) return (b.item.endedAt ?? b.item.createdAt).localeCompare(a.item.endedAt ?? a.item.createdAt);
      return a.i - b.i; // keep the queue order
    })
    .map((x) => x.item);
}

export interface TabCounts {
  total: number;
  active: number;
  failed: number;
}

export function tabCounts(snapshot: TransfersSnapshotDto | null): Record<TransferTab, TabCounts> {
  const count = (tab: TransferTab): TabCounts => {
    const items = tabItems(snapshot, tab);
    return {
      total: items.length,
      active: items.filter((i) => stateGroup(i) === 'active').length,
      failed: items.filter((i) => stateGroup(i) === 'failed').length
    };
  };
  return { downloads: count('downloads'), uploads: count('uploads'), tasks: count('tasks') };
}

/**
 * What the user can do with a transfer:
 * pause (running downloads only), resume (paused → retry), cancel (running or
 * queued), retry (failed/cancelled downloads and tasks), remove (anything not running).
 */
export function availableActions(t: TransferDto): TransferAction[] {
  const g = stateGroup(t);
  const out: TransferAction[] = [];
  const retryable = t.kind === 'download' || t.kind === 'task';
  if (g === 'active') {
    if (t.kind === 'download') out.push('pause');
    out.push('cancel');
    return out;
  }
  if (g === 'paused') {
    if (retryable) out.push('resume');
    out.push('cancel', 'remove');
    return out;
  }
  if (g === 'queued') return ['cancel', 'remove'];
  if ((g === 'failed' || g === 'canceled') && retryable) out.push('retry');
  out.push('remove');
  return out;
}

// ---------------------------------------------------------------- speed / ETA

interface Sample {
  bytes: number;
  at: number;
  speed: number;
}

/**
 * Per-item speed from consecutive snapshots (exponential moving average, so
 * the numbers do not jump around), and the ETA derived from it.
 */
export class SpeedTracker {
  private samples = new Map<string, Sample>();
  private readonly smoothing: number;
  constructor(smoothing = 0.35) {
    this.smoothing = smoothing;
  }

  update(items: TransferDto[], now = Date.now()): void {
    const alive = new Set<string>();
    for (const item of items) {
      alive.add(item.id);
      const prev = this.samples.get(item.id);
      if (stateGroup(item) !== 'active') {
        this.samples.set(item.id, { bytes: item.transmitted, at: now, speed: 0 });
        continue;
      }
      if (!prev) {
        this.samples.set(item.id, { bytes: item.transmitted, at: now, speed: 0 });
        continue;
      }
      const dt = (now - prev.at) / 1000;
      if (dt < 0.25) continue; // too close to measure
      const instant = Math.max(0, (item.transmitted - prev.bytes) / dt);
      const speed = prev.speed === 0 ? instant : prev.speed + this.smoothing * (instant - prev.speed);
      this.samples.set(item.id, { bytes: item.transmitted, at: now, speed });
    }
    for (const id of [...this.samples.keys()]) if (!alive.has(id)) this.samples.delete(id);
  }

  speed(id: string): number {
    return this.samples.get(id)?.speed ?? 0;
  }

  /** Seconds left, or null when unknown. */
  eta(item: TransferDto): number | null {
    const speed = this.speed(item.id);
    if (stateGroup(item) !== 'active' || speed <= 0 || item.size <= 0) return null;
    return Math.max(0, (item.size - item.transmitted) / speed);
  }
}

/** 3725 → "1 h 2 min", 75 → "1 min 15 s", 9 → "9 s". */
export function formatEta(seconds: number | null): string {
  if (seconds === null || !Number.isFinite(seconds)) return '';
  const s = Math.round(seconds);
  if (s < 60) return `${s} s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m} min ${String(s % 60).padStart(2, '0')} s`;
  const h = Math.floor(m / 60);
  if (h < 48) return `${h} h ${m % 60} min`;
  return `${Math.floor(h / 24)} d ${h % 24} h`;
}

// ---------------------------------------------------------------- speed history

export interface SpeedSeries {
  /** epoch ms */
  t: number;
  down: number;
  up: number;
}

/** Merge the history from `GetSpeedHistory` into chart points. */
export function historyToSeries(history: SpeedHistoryDto): SpeedSeries[] {
  const byTime = new Map<number, SpeedSeries>();
  const add = (points: SpeedPointDto[], key: 'down' | 'up') => {
    for (const p of points) {
      const time = Date.parse(p.time);
      if (!Number.isFinite(time)) continue;
      const entry = byTime.get(time) ?? { t: time, down: 0, up: 0 };
      entry[key] = p.bytesPerSecond;
      byTime.set(time, entry);
    }
  };
  add(history.download ?? [], 'down');
  add(history.upload ?? [], 'up');
  return [...byTime.values()].sort((a, b) => a.t - b.t);
}

/** Append one live sample and drop what falls out of the window. */
export function appendSample(series: SpeedSeries[], down: SpeedPointDto, up: SpeedPointDto, windowSeconds: number): SpeedSeries[] {
  const time = Date.parse(down.time) || Date.parse(up.time) || Date.now();
  const last = series[series.length - 1];
  const next = last && last.t >= time ? series.slice(0, -1) : series.slice();
  next.push({ t: time, down: down.bytesPerSecond, up: up.bytesPerSecond });
  const from = time - windowSeconds * 1000;
  let start = 0;
  while (start < next.length && next[start].t < from) start++;
  return start ? next.slice(start) : next;
}
