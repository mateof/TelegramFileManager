import type { EventsHubMessages } from '../types/events';
import type { SpeedHistoryDto, SpeedPointDto, TransferSummaryDto, TransfersSnapshotDto } from '../types/transfers';
import { TypedHub, type HubOptions } from './hub';

export { TypedHub } from './hub';
export type { HubMessageMap, HubOptions, HubStatus } from './hub';

export const TRANSFERS_HUB_PATH = '/hubs/transfers';
export const EVENTS_HUB_PATH = '/hubs/events';

/** Server → client messages of /hubs/transfers (see docs/api/signalr.md). */
export interface TransfersHubMessages {
  [message: string]: unknown[];
  TransfersSnapshot: [snapshot: TransfersSnapshotDto];
  TransferSummary: [summary: TransferSummaryDto];
  SpeedHistoryPoint: [download: SpeedPointDto, upload: SpeedPointDto];
}

export class TransfersHub extends TypedHub<TransfersHubMessages> {
  getSnapshot(): Promise<TransfersSnapshotDto> {
    return this.invoke<TransfersSnapshotDto>('GetSnapshot');
  }
  getSummary(): Promise<TransferSummaryDto> {
    return this.invoke<TransferSummaryDto>('GetSummary');
  }
  getSpeedHistory(): Promise<SpeedHistoryDto> {
    return this.invoke<SpeedHistoryDto>('GetSpeedHistory');
  }
  /** Stop full snapshots (summaries keep coming): for screens that only show a badge. */
  muteSnapshots(): Promise<void> {
    return this.invoke('MuteSnapshots');
  }
  unmuteSnapshots(): Promise<void> {
    return this.invoke('UnmuteSnapshots');
  }
  muteSpeedHistory(): Promise<void> {
    return this.invoke('MuteSpeedHistory');
  }
  unmuteSpeedHistory(): Promise<void> {
    return this.invoke('UnmuteSpeedHistory');
  }
}

/** Server → client messages of /hubs/events, as argument tuples. */
export type EventsHubArgs = { [K in keyof EventsHubMessages]: [payload: EventsHubMessages[K]] } & {
  [message: string]: unknown[];
};

export class EventsHub extends TypedHub<EventsHubArgs> {
  private metricsSubscribers = 0;

  constructor(path: string, options?: HubOptions) {
    super(path, options);
    // Metrics subscriptions live on the server per connection: renew them after a reconnect
    this.onReconnected(() => {
      if (this.metricsSubscribers > 0) this.send('SubscribeMetrics');
    });
  }

  /** Start receiving `Metrics` messages; call the returned function to stop. Ref-counted. */
  subscribeMetrics(): () => void {
    this.metricsSubscribers++;
    if (this.metricsSubscribers === 1) void this.invoke('SubscribeMetrics').catch(() => undefined);
    let done = false;
    return () => {
      if (done) return;
      done = true;
      this.metricsSubscribers--;
      if (this.metricsSubscribers === 0) void this.invoke('UnsubscribeMetrics').catch(() => undefined);
    };
  }
}

/** Connection to /hubs/transfers (not started: call `start()`). */
export function createTransfersConnection(options?: HubOptions): TransfersHub {
  return new TransfersHub(TRANSFERS_HUB_PATH, options);
}

/** Connection to /hubs/events (not started: call `start()`). */
export function createEventsConnection(options?: HubOptions): EventsHub {
  return new EventsHub(EVENTS_HUB_PATH, options);
}
