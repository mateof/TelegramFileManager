import type { EventNotification, EventsHubMessages, TransferSummaryDto } from '@tfm/api-client';
import type { EventsHub, HubStatus, TransfersHub, TransfersHubMessages } from '@tfm/api-client/signalr';
import { toast } from '@tfm/ui';
import { useEffect, useRef } from 'react';
import { create } from 'zustand';

/**
 * App-wide SignalR connections (/hubs/transfers and /hubs/events). They are
 * started by <TelegramGate> once the user is signed in, and shared by every
 * feature. The SignalR client is loaded lazily (own chunk, off the initial
 * bundle), so always go through these helpers:
 *
 * ```ts
 * useEventsMessage('ChannelRefreshed', (e) => ...);
 * useTransfersMessage('TransfersSnapshot', (s) => ...);
 * const summary = useRealtime((s) => s.transferSummary);
 * const hub = await getTransfersHub(); await hub.getSpeedHistory();
 * ```
 */

interface Hubs {
  transfers: TransfersHub;
  events: EventsHub;
}

let hubsPromise: Promise<Hubs> | null = null;

function loadHubs(): Promise<Hubs> {
  hubsPromise ??= import('@tfm/api-client/signalr').then((m) => ({
    transfers: m.createTransfersConnection(),
    events: m.createEventsConnection()
  }));
  return hubsPromise;
}

/** The /hubs/transfers connection (loads the SignalR client on first use). */
export async function getTransfersHub(): Promise<TransfersHub> {
  return (await loadHubs()).transfers;
}

/** The /hubs/events connection (e.g. `(await getEventsHub()).subscribeMetrics()`). */
export async function getEventsHub(): Promise<EventsHub> {
  return (await loadHubs()).events;
}

export interface StoredNotification extends EventNotification {
  id: number;
  read: boolean;
}

interface RealtimeState {
  transfersStatus: HubStatus;
  eventsStatus: HubStatus;
  transferSummary: TransferSummaryDto | null;
  notifications: StoredNotification[];
  pushNotification: (n: EventNotification) => void;
  markAllRead: () => void;
  clearNotifications: () => void;
}

const MAX_NOTIFICATIONS = 50;
let nextNotificationId = 1;

export const useRealtime = create<RealtimeState>()((set) => ({
  transfersStatus: 'idle',
  eventsStatus: 'idle',
  transferSummary: null,
  notifications: [],
  pushNotification: (n) =>
    set((s) => ({
      notifications: [{ ...n, id: nextNotificationId++, read: false }, ...s.notifications].slice(0, MAX_NOTIFICATIONS)
    })),
  markAllRead: () => set((s) => ({ notifications: s.notifications.map((n) => ({ ...n, read: true })) })),
  clearNotifications: () => set({ notifications: [] })
}));

function showToast(n: EventNotification) {
  const opts = n.title ? { description: n.message } : undefined;
  const text = n.title ?? n.message;
  if (n.level === 'error') toast.error(text, opts);
  else if (n.level === 'warning') toast.warning(text, opts);
  else if (n.level === 'success') toast.success(text, opts);
  else toast(text, opts);
}

let started = false;
const cleanups: (() => void)[] = [];

/** Starts both hubs (idempotent). Called by TelegramGate. */
export function startRealtime(): void {
  if (started) return;
  started = true;
  void loadHubs().then(({ transfers, events }) => {
    if (!started) return;
    cleanups.push(
      transfers.onStatus((status) => useRealtime.setState({ transfersStatus: status })),
      events.onStatus((status) => useRealtime.setState({ eventsStatus: status })),
      transfers.on('TransferSummary', (summary) => useRealtime.setState({ transferSummary: summary })),
      transfers.on('TransfersSnapshot', (snapshot) => useRealtime.setState({ transferSummary: snapshot.summary })),
      events.on('Notification', (n) => {
        useRealtime.getState().pushNotification(n);
        showToast(n);
      })
    );
    void transfers.start();
    void events.start();
  });
}

/** Stops both hubs (sign out). */
export async function stopRealtime(): Promise<void> {
  if (!started) return;
  started = false;
  cleanups.splice(0).forEach((c) => c());
  if (hubsPromise) {
    const { transfers, events } = await hubsPromise;
    await Promise.allSettled([transfers.stop(), events.stop()]);
  }
  useRealtime.setState({ transferSummary: null });
}

/** Subscribe a component to a /hubs/events message (the latest handler is always used). */
export function useEventsMessage<K extends keyof EventsHubMessages & string>(
  message: K,
  handler: (payload: EventsHubMessages[K]) => void
): void {
  const ref = useRef(handler);
  useEffect(() => {
    ref.current = handler;
  });
  useEffect(() => {
    let off: (() => void) | undefined;
    let cancelled = false;
    void loadHubs().then(({ events }) => {
      if (!cancelled) off = events.on(message, (payload) => ref.current(payload as EventsHubMessages[K]));
    });
    return () => {
      cancelled = true;
      off?.();
    };
  }, [message]);
}

/** Subscribe a component to a /hubs/transfers message. */
export function useTransfersMessage<K extends keyof TransfersHubMessages & string>(
  message: K,
  handler: (...args: TransfersHubMessages[K]) => void
): void {
  const ref = useRef(handler);
  useEffect(() => {
    ref.current = handler;
  });
  useEffect(() => {
    let off: (() => void) | undefined;
    let cancelled = false;
    void loadHubs().then(({ transfers }) => {
      if (!cancelled) off = transfers.on(message, (...args) => ref.current(...(args as TransfersHubMessages[K])));
    });
    return () => {
      cancelled = true;
      off?.();
    };
  }, [message]);
}
