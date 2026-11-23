import type { TransfersSnapshotDto } from '@tfm/api-client';
import { useCallback, useEffect, useState } from 'react';
import { api } from '@/lib/api';
import { useTransfersMessage } from '@/lib/realtime';
import { appendSample, historyToSeries, SpeedTracker, type SpeedSeries } from './model';

export interface ItemMetrics {
  speed: number;
  eta: number | null;
}

/**
 * Live transfers state: the first snapshot from REST (so the page renders
 * even before the hub connects), then every `TransfersSnapshot` /
 * `SpeedHistoryPoint` pushed by /hubs/transfers.
 */
export function useTransfersLive() {
  const [tracker] = useState(() => new SpeedTracker());
  const [state, setState] = useState<{ snapshot: TransfersSnapshotDto | null; metrics: Record<string, ItemMetrics> }>({ snapshot: null, metrics: {} });
  const [series, setSeries] = useState<SpeedSeries[]>([]);
  const [windowSeconds, setWindowSeconds] = useState(600);
  const [error, setError] = useState<unknown>(null);

  const apply = useCallback(
    (snapshot: TransfersSnapshotDto) => {
      const all = [...snapshot.downloads, ...snapshot.uploads, ...snapshot.tasks, ...snapshot.queuedDownloads, ...snapshot.queuedUploads];
      tracker.update(all);
      const metrics: Record<string, ItemMetrics> = {};
      for (const item of all) metrics[item.id] = { speed: tracker.speed(item.id), eta: tracker.eta(item) };
      setState({ snapshot, metrics });
      setError(null);
    },
    [tracker]
  );

  const refresh = useCallback(
    async (signal?: AbortSignal) => {
      try {
        apply(await api.transfers.snapshot(signal));
      } catch (e) {
        if (!signal?.aborted) setError(e);
      }
    },
    [apply]
  );

  useEffect(() => {
    const ctrl = new AbortController();
    api.transfers
      .snapshot(ctrl.signal)
      .then(apply)
      .catch((e: unknown) => {
        if (!ctrl.signal.aborted) setError(e);
      });
    api.transfers
      .speedHistory(ctrl.signal)
      .then((h) => {
        setSeries(historyToSeries(h));
        if (h.windowSeconds > 0) setWindowSeconds(h.windowSeconds);
      })
      .catch(() => undefined);
    return () => ctrl.abort();
  }, [apply]);

  useTransfersMessage('TransfersSnapshot', apply);
  useTransfersMessage('SpeedHistoryPoint', (down, up) => setSeries((s) => appendSample(s, down, up, windowSeconds)));

  return { snapshot: state.snapshot, metrics: state.metrics, series, windowSeconds, error, refresh };
}
