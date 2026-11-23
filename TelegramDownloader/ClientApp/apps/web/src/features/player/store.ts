import { create } from 'zustand';
import { persist, type PersistStorage, type StorageValue } from 'zustand/middleware';
import type { AudioTrack } from './public';
import {
  currentTrack,
  cycleRepeat,
  EMPTY_QUEUE,
  enqueue,
  moveInQueue,
  removeFromQueue,
  replaceQueue,
  setShuffle,
  type QueueState,
  type QueuedTrack,
  type RepeatMode
} from './queue';

/**
 * State of the global audio player. Small on purpose: it is in the initial
 * bundle (the layout's PlayerSlot reads it to know whether to show the bar).
 * The playback engine (engine.ts) and the UI are lazy and drive it.
 *
 * Persisted: the queue, the current index, where it was (resumed *paused*
 * after a reload), shuffle/repeat, volume and crossfade.
 */

export type PlayerStatus = 'idle' | 'loading' | 'playing' | 'paused' | 'buffering' | 'error';

export interface TimeRange {
  start: number;
  end: number;
}

export interface PlayerState extends QueueState {
  status: PlayerStatus;
  position: number;
  duration: number;
  buffered: TimeRange[];
  error: string | null;
  /** Position saved for the session restore (seconds). */
  savedPosition: number;
  volume: number;
  muted: boolean;
  /** Crossfade between tracks in seconds (0 = gapless, no fade). */
  crossfadeSeconds: number;
  /** Sleep timer: epoch ms when playback pauses, or "end of the current track". */
  sleepEndsAt: number | null;
  sleepAtEndOfTrack: boolean;
  expanded: boolean;
  queueOpen: boolean;

  // queue edits (the engine reacts to the current track changing)
  replaceQueue: (tracks: AudioTrack[], startIndex?: number) => void;
  enqueue: (tracks: AudioTrack[], position: 'next' | 'end') => void;
  remove: (qid: string) => boolean;
  move: (from: number, to: number) => void;
  setIndex: (index: number) => void;
  toggleShuffle: () => void;
  cycleRepeat: () => void;
  setRepeat: (mode: RepeatMode) => void;
  clear: () => void;

  // UI
  setExpanded: (expanded: boolean) => void;
  setQueueOpen: (open: boolean) => void;
  setVolume: (volume: number) => void;
  setMuted: (muted: boolean) => void;
  setCrossfade: (seconds: number) => void;
  setSleep: (endsAt: number | null, atEndOfTrack: boolean) => void;
}

export const PLAYER_STORAGE_KEY = 'tfm.web.player';

const clamp01 = (v: number) => Math.max(0, Math.min(1, v));

// The position changes several times per second while playing: write the
// (possibly long) queue to localStorage at most once per second, and on pagehide
let pendingWrite: { name: string; value: StorageValue<unknown> } | null = null;
let writeTimer: ReturnType<typeof setTimeout> | null = null;

/** Write the pending player state now (tests, page hide). */
export function flushPlayerStorage(): void {
  if (writeTimer) clearTimeout(writeTimer);
  writeTimer = null;
  const w = pendingWrite;
  pendingWrite = null;
  if (!w) return;
  try {
    localStorage.setItem(w.name, JSON.stringify(w.value));
  } catch {
    // quota / private mode: the session is simply not restored
  }
}

const throttledStorage: PersistStorage<unknown> = {
  getItem: (name) => {
    try {
      const raw = localStorage.getItem(name);
      return raw ? (JSON.parse(raw) as StorageValue<unknown>) : null;
    } catch {
      return null;
    }
  },
  setItem: (name, value) => {
    pendingWrite = { name, value };
    writeTimer ??= setTimeout(flushPlayerStorage, 1000);
  },
  removeItem: (name) => {
    pendingWrite = null;
    try {
      localStorage.removeItem(name);
    } catch {
      // ignore
    }
  }
};

if (typeof window !== 'undefined') window.addEventListener('pagehide', flushPlayerStorage);

export const usePlayer = create<PlayerState>()(
  persist(
    (set, get) => ({
      ...EMPTY_QUEUE,
      status: 'idle',
      position: 0,
      duration: 0,
      buffered: [],
      error: null,
      savedPosition: 0,
      volume: 1,
      muted: false,
      crossfadeSeconds: 0,
      sleepEndsAt: null,
      sleepAtEndOfTrack: false,
      expanded: false,
      queueOpen: false,

      replaceQueue: (tracks, startIndex = 0) =>
        set((s) => ({ ...replaceQueue(s, tracks, startIndex), position: 0, savedPosition: 0, duration: 0, error: null })),
      enqueue: (tracks, position) => set((s) => enqueue(s, tracks, position)),
      remove: (qid) => {
        const result = removeFromQueue(get(), qid);
        set(result.state);
        return result.removedCurrent;
      },
      move: (from, to) => set((s) => moveInQueue(s, from, to)),
      setIndex: (index) => set((s) => (index >= 0 && index < s.queue.length ? { index } : {})),
      toggleShuffle: () => set((s) => setShuffle(s, !s.shuffle)),
      cycleRepeat: () => set((s) => ({ repeat: cycleRepeat(s.repeat) })),
      setRepeat: (repeat) => set({ repeat }),
      clear: () =>
        set({
          queue: [],
          index: -1,
          originalOrder: null,
          status: 'idle',
          position: 0,
          duration: 0,
          savedPosition: 0,
          buffered: [],
          error: null,
          expanded: false,
          sleepEndsAt: null,
          sleepAtEndOfTrack: false
        }),

      setExpanded: (expanded) => set({ expanded }),
      setQueueOpen: (queueOpen) => set({ queueOpen }),
      setVolume: (volume) => set({ volume: clamp01(volume), muted: volume <= 0 ? get().muted : false }),
      setMuted: (muted) => set({ muted }),
      setCrossfade: (crossfadeSeconds) => set({ crossfadeSeconds: Math.max(0, Math.min(12, crossfadeSeconds)) }),
      setSleep: (sleepEndsAt, sleepAtEndOfTrack) => set({ sleepEndsAt, sleepAtEndOfTrack })
    }),
    {
      name: PLAYER_STORAGE_KEY,
      version: 1,
      storage: throttledStorage as PersistStorage<Partial<PlayerState>>,
      partialize: (s) => ({
        queue: s.queue,
        index: s.index,
        shuffle: s.shuffle,
        originalOrder: s.originalOrder,
        repeat: s.repeat,
        savedPosition: s.savedPosition,
        duration: s.duration,
        volume: s.volume,
        muted: s.muted,
        crossfadeSeconds: s.crossfadeSeconds
      }),
      // A restored session is shown paused at the saved position: nothing plays until the user presses play
      merge: (persisted, current) => {
        const p = (persisted ?? {}) as Partial<PlayerState>;
        const queue = Array.isArray(p.queue) ? p.queue : [];
        const index = typeof p.index === 'number' && p.index < queue.length ? p.index : queue.length ? 0 : -1;
        return {
          ...current,
          ...p,
          queue,
          index,
          status: queue.length ? 'paused' : 'idle',
          position: p.savedPosition ?? 0
        };
      }
    }
  )
);

export function selectCurrent(s: PlayerState): QueuedTrack | null {
  return currentTrack(s);
}

/** True when there is something in the player (the bar is shown). */
export function selectHasQueue(s: PlayerState): boolean {
  return s.queue.length > 0;
}
