import { beforeEach, describe, expect, it } from 'vitest';
import type { AudioTrack } from './public';
import {
  cycleRepeat,
  displayTitle,
  EMPTY_QUEUE,
  enqueue,
  moveInQueue,
  nextIndex,
  previousIndex,
  removeFromQueue,
  replaceQueue,
  setShuffle,
  type QueueState
} from './queue';
import { flushPlayerStorage, PLAYER_STORAGE_KEY, usePlayer } from './store';

const tracks = (...ids: string[]): AudioTrack[] => ids.map((id) => ({ id, title: id, url: `/u/${id}` }));
const ids = (s: QueueState) => s.queue.map((t) => t.id);
const current = (s: QueueState) => s.queue[s.index]?.id;
// Deterministic "random": reverses the order with Fisher-Yates when it always returns 0
const zero = () => 0;

describe('queue', () => {
  it('replaces the queue and starts at the given index', () => {
    const s = replaceQueue(EMPTY_QUEUE, tracks('a', 'b', 'c'), 1);
    expect(ids(s)).toEqual(['a', 'b', 'c']);
    expect(current(s)).toBe('b');
    expect(new Set(s.queue.map((t) => t.qid)).size).toBe(3);
  });

  it('with shuffle on, the chosen track goes first and the rest is shuffled', () => {
    const s = replaceQueue({ ...EMPTY_QUEUE, shuffle: true }, tracks('a', 'b', 'c', 'd'), 2, zero);
    expect(current(s)).toBe('c');
    expect(s.index).toBe(0);
    expect(ids(s).slice().sort()).toEqual(['a', 'b', 'c', 'd']);
    expect(s.originalOrder).toHaveLength(4);
  });

  it('computes next/previous with every repeat mode', () => {
    const base = replaceQueue(EMPTY_QUEUE, tracks('a', 'b', 'c'), 2);
    expect(nextIndex(base)).toBe(-1);
    expect(nextIndex({ ...base, repeat: 'all' })).toBe(0);
    expect(nextIndex({ ...base, repeat: 'one' }, true)).toBe(2);
    expect(nextIndex({ ...base, repeat: 'one' }, false)).toBe(0);
    const first = { ...base, index: 0 };
    expect(previousIndex(first)).toBe(-1);
    expect(previousIndex({ ...first, repeat: 'all' })).toBe(2);
    expect(previousIndex(base)).toBe(1);
    expect(nextIndex(EMPTY_QUEUE)).toBe(-1);
  });

  it('enqueues next or at the end', () => {
    const s = replaceQueue(EMPTY_QUEUE, tracks('a', 'b'), 0);
    expect(ids(enqueue(s, tracks('x'), 'next'))).toEqual(['a', 'x', 'b']);
    expect(ids(enqueue(s, tracks('x', 'y'), 'end'))).toEqual(['a', 'b', 'x', 'y']);
    const fromEmpty = enqueue(EMPTY_QUEUE, tracks('x'), 'end');
    expect(current(fromEmpty)).toBe('x');
  });

  it('keeps the index on the current track when removing', () => {
    const s = replaceQueue(EMPTY_QUEUE, tracks('a', 'b', 'c'), 1);
    const before = removeFromQueue(s, s.queue[0].qid);
    expect(current(before.state)).toBe('b');
    expect(before.removedCurrent).toBe(false);
    const self = removeFromQueue(s, s.queue[1].qid);
    expect(self.removedCurrent).toBe(true);
    expect(current(self.state)).toBe('c');
    const last = replaceQueue(EMPTY_QUEUE, tracks('a', 'b'), 1);
    expect(current(removeFromQueue(last, last.queue[1].qid).state)).toBe('a');
    const only = replaceQueue(EMPTY_QUEUE, tracks('a'), 0);
    expect(removeFromQueue(only, only.queue[0].qid).state.index).toBe(-1);
  });

  it('moves entries and follows the current track', () => {
    const s = replaceQueue(EMPTY_QUEUE, tracks('a', 'b', 'c', 'd'), 1);
    const moved = moveInQueue(s, 1, 3);
    expect(ids(moved)).toEqual(['a', 'c', 'd', 'b']);
    expect(current(moved)).toBe('b');
    const other = moveInQueue(s, 3, 0);
    expect(ids(other)).toEqual(['d', 'a', 'b', 'c']);
    expect(current(other)).toBe('b');
    expect(moveInQueue(s, 0, 9)).toBe(s);
  });

  it('shuffles only what comes next and restores the original order', () => {
    const s = replaceQueue(EMPTY_QUEUE, tracks('a', 'b', 'c', 'd', 'e'), 1);
    const on = setShuffle(s, true, zero);
    expect(ids(on).slice(0, 2)).toEqual(['a', 'b']);
    expect(ids(on).slice(2).sort()).toEqual(['c', 'd', 'e']);
    // add while shuffled, then turn it off
    const added = enqueue(on, tracks('x'), 'next');
    const off = setShuffle(added, false);
    expect(ids(off)).toEqual(['a', 'b', 'x', 'c', 'd', 'e']);
    expect(current(off)).toBe('b');
    expect(off.originalOrder).toBeNull();
  });

  it('cycles repeat modes and cleans titles', () => {
    expect(cycleRepeat('off')).toBe('all');
    expect(cycleRepeat('all')).toBe('one');
    expect(cycleRepeat('one')).toBe('off');
    expect(displayTitle({ title: 'Song.FLAC' })).toBe('Song');
    expect(displayTitle({ title: 'v1.2 mix' })).toBe('v1.2 mix');
  });
});

describe('player store', () => {
  beforeEach(() => {
    usePlayer.getState().clear();
    usePlayer.setState({ shuffle: false, originalOrder: null, repeat: 'off' });
  });

  it('persists the queue, position and preferences but not the runtime state', () => {
    usePlayer.getState().replaceQueue(tracks('a', 'b'), 1);
    usePlayer.setState({ savedPosition: 33, volume: 0.5, status: 'playing', position: 34, expanded: true });
    flushPlayerStorage();
    const saved = JSON.parse(localStorage.getItem(PLAYER_STORAGE_KEY) ?? '{}') as { state: Record<string, unknown> };
    expect(saved.state.index).toBe(1);
    expect(saved.state.savedPosition).toBe(33);
    expect(saved.state.volume).toBe(0.5);
    expect(saved.state).not.toHaveProperty('status');
    expect(saved.state).not.toHaveProperty('expanded');
  });

  it('restores a saved session paused at the saved position', async () => {
    usePlayer.getState().replaceQueue(tracks('a', 'b'), 1);
    usePlayer.setState({ savedPosition: 12, status: 'playing' });
    flushPlayerStorage();
    await usePlayer.persist.rehydrate();
    const s = usePlayer.getState();
    expect(s.status).toBe('paused');
    expect(s.position).toBe(12);
    expect(s.queue[s.index].id).toBe('b');
  });

  it('clamps the volume and the crossfade', () => {
    usePlayer.getState().setVolume(3);
    expect(usePlayer.getState().volume).toBe(1);
    usePlayer.getState().setCrossfade(60);
    expect(usePlayer.getState().crossfadeSeconds).toBe(12);
  });
});
