import type { AudioTrack } from './public';

/**
 * Pure queue logic of the audio player (no DOM, no store): the store applies
 * these functions and the tests exercise them directly.
 *
 * Shuffle reorders the *upcoming* part of the queue (what has been played and
 * the current track stay put), like modern music apps, so the queue panel
 * always shows the real play order and next/previous are linear. The original
 * order is remembered and restored when shuffle is turned off.
 */

/** A track in the queue. `qid` is unique per entry (the same file can be queued twice). */
export interface QueuedTrack extends AudioTrack {
  qid: string;
}

export type RepeatMode = 'off' | 'all' | 'one';

export interface QueueState {
  queue: QueuedTrack[];
  /** Index of the current track, -1 when the queue is empty. */
  index: number;
  shuffle: boolean;
  /** qids in the order they had before shuffling (null while shuffle is off). */
  originalOrder: string[] | null;
  repeat: RepeatMode;
}

export const EMPTY_QUEUE: QueueState = { queue: [], index: -1, shuffle: false, originalOrder: null, repeat: 'off' };

let qidCounter = 0;
export function makeQid(): string {
  qidCounter += 1;
  return `${Date.now().toString(36)}-${qidCounter.toString(36)}`;
}

export function toQueued(tracks: AudioTrack[]): QueuedTrack[] {
  return tracks.map((t) => ({ ...t, qid: makeQid() }));
}

/** Fisher-Yates with an injectable random source (tests). */
export function shuffled<T>(items: T[], random: () => number = Math.random): T[] {
  const out = items.slice();
  for (let i = out.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [out[i], out[j]] = [out[j], out[i]];
  }
  return out;
}

export function currentTrack(state: Pick<QueueState, 'queue' | 'index'>): QueuedTrack | null {
  return state.index >= 0 && state.index < state.queue.length ? state.queue[state.index] : null;
}

/** Replace the queue and start at `startIndex` (with shuffle on, that track goes first and the rest is shuffled). */
export function replaceQueue(state: QueueState, tracks: AudioTrack[], startIndex = 0, random?: () => number): QueueState {
  const queued = toQueued(tracks);
  if (queued.length === 0) return { ...state, queue: [], index: -1, originalOrder: state.shuffle ? [] : null };
  const start = Math.min(Math.max(0, startIndex), queued.length - 1);
  if (!state.shuffle) return { ...state, queue: queued, index: start, originalOrder: null };
  const first = queued[start];
  const rest = shuffled(queued.filter((_, i) => i !== start), random);
  return { ...state, queue: [first, ...rest], index: 0, originalOrder: queued.map((t) => t.qid) };
}

/**
 * Index that plays after the current one, or -1 at the end of the queue.
 * `auto` = the track ended by itself (repeat-one replays it; a manual "next" skips).
 */
export function nextIndex(state: QueueState, auto = false): number {
  const { queue, index, repeat } = state;
  if (queue.length === 0) return -1;
  if (auto && repeat === 'one') return index;
  if (index < queue.length - 1) return index + 1;
  return repeat === 'all' || repeat === 'one' ? 0 : -1;
}

/** Index of the previous track, or -1 when there is none. */
export function previousIndex(state: QueueState): number {
  const { queue, index, repeat } = state;
  if (queue.length === 0) return -1;
  if (index > 0) return index - 1;
  return repeat !== 'off' ? queue.length - 1 : -1;
}

/** Add tracks right after the current one ("play next") or at the end. */
export function enqueue(state: QueueState, tracks: AudioTrack[], position: 'next' | 'end'): QueueState {
  const added = toQueued(tracks);
  if (added.length === 0) return state;
  if (state.queue.length === 0) {
    return { ...state, queue: added, index: 0, originalOrder: state.shuffle ? added.map((t) => t.qid) : null };
  }
  const at = position === 'next' ? state.index + 1 : state.queue.length;
  const queue = [...state.queue.slice(0, at), ...added, ...state.queue.slice(at)];
  let originalOrder = state.originalOrder;
  if (originalOrder) {
    const ids = added.map((t) => t.qid);
    const current = currentTrack(state);
    const pos = position === 'next' && current ? originalOrder.indexOf(current.qid) + 1 : originalOrder.length;
    originalOrder = [...originalOrder.slice(0, pos), ...ids, ...originalOrder.slice(pos)];
  }
  return { ...state, queue, originalOrder };
}

/** Remove one entry. `removedCurrent` tells the player to load what is now current. */
export function removeFromQueue(state: QueueState, qid: string): { state: QueueState; removedCurrent: boolean } {
  const at = state.queue.findIndex((t) => t.qid === qid);
  if (at < 0) return { state, removedCurrent: false };
  const queue = state.queue.filter((_, i) => i !== at);
  const removedCurrent = at === state.index;
  let index = state.index;
  if (at < state.index) index -= 1;
  if (queue.length === 0) index = -1;
  else if (index >= queue.length) index = state.repeat === 'all' ? 0 : queue.length - 1;
  const originalOrder = state.originalOrder?.filter((id) => id !== qid) ?? null;
  return { state: { ...state, queue, index, originalOrder }, removedCurrent };
}

/** Move the entry at `from` to `to` (the current track keeps playing; the index follows it). */
export function moveInQueue(state: QueueState, from: number, to: number): QueueState {
  const n = state.queue.length;
  if (from === to || from < 0 || to < 0 || from >= n || to >= n) return state;
  const queue = state.queue.slice();
  const [item] = queue.splice(from, 1);
  queue.splice(to, 0, item);
  const current = currentTrack(state);
  const index = current ? queue.findIndex((t) => t.qid === current.qid) : state.index;
  // A manual reorder while shuffled becomes the new order to come back to
  const originalOrder = state.originalOrder ? queue.map((t) => t.qid) : null;
  return { ...state, queue, index, originalOrder };
}

/** Turn shuffle on (shuffles what comes after the current track) or off (restores the original order). */
export function setShuffle(state: QueueState, on: boolean, random?: () => number): QueueState {
  if (on === state.shuffle) return state;
  if (on) {
    const head = state.queue.slice(0, state.index + 1);
    const rest = shuffled(state.queue.slice(state.index + 1), random);
    return { ...state, shuffle: true, originalOrder: state.queue.map((t) => t.qid), queue: [...head, ...rest] };
  }
  const order = state.originalOrder ?? [];
  const byId = new Map(state.queue.map((t) => [t.qid, t]));
  const restored: QueuedTrack[] = [];
  for (const id of order) {
    const t = byId.get(id);
    if (t) {
      restored.push(t);
      byId.delete(id);
    }
  }
  restored.push(...byId.values()); // anything not in the remembered order
  const current = currentTrack(state);
  const index = current ? restored.findIndex((t) => t.qid === current.qid) : -1;
  return { ...state, shuffle: false, originalOrder: null, queue: restored, index };
}

export function cycleRepeat(mode: RepeatMode): RepeatMode {
  return mode === 'off' ? 'all' : mode === 'all' ? 'one' : 'off';
}

/** Title to show: the tag, or the file name without its extension. */
export function displayTitle(track: Pick<AudioTrack, 'title'>): string {
  return track.title.replace(/\.(mp3|flac|m4a|aac|ogg|oga|opus|wav|wma|alac|aiff?|ape|webm|mka)$/i, '');
}
