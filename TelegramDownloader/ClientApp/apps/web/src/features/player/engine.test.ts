import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AudioEngine } from './engine';
import type { AudioTrack } from './public';
import { usePlayer } from './store';

// ---- Fake media element ----------------------------------------------------
// jsdom has no media playback: emulate just enough of HTMLMediaElement.
interface FakeState {
  paused: boolean;
  currentTime: number;
  duration: number;
  ended: boolean;
}
const state = new WeakMap<HTMLMediaElement, FakeState>();
const st = (el: HTMLMediaElement) => {
  let s = state.get(el);
  if (!s) {
    s = { paused: true, currentTime: 0, duration: NaN, ended: false };
    state.set(el, s);
  }
  return s;
};
// When true, play() resolves but 'playing' is only fired by the test
let holdPlaying = false;

beforeEach(() => {
  const proto = window.HTMLMediaElement.prototype;
  Object.defineProperty(proto, 'paused', { configurable: true, get() { return st(this).paused; } });
  Object.defineProperty(proto, 'ended', { configurable: true, get() { return st(this).ended; } });
  Object.defineProperty(proto, 'duration', { configurable: true, get() { return st(this).duration; } });
  Object.defineProperty(proto, 'readyState', { configurable: true, get() { return Number.isFinite(st(this).duration) ? 1 : 0; } });
  Object.defineProperty(proto, 'currentTime', {
    configurable: true,
    get() { return st(this).currentTime; },
    set(v: number) { st(this).currentTime = v; }
  });
  proto.play = function (this: HTMLMediaElement) {
    st(this).paused = false;
    this.dispatchEvent(new Event('play'));
    if (!holdPlaying) this.dispatchEvent(new Event('playing'));
    return Promise.resolve();
  };
  proto.pause = function (this: HTMLMediaElement) {
    if (!st(this).paused) {
      st(this).paused = true;
      this.dispatchEvent(new Event('pause'));
    }
  };
  proto.load = function (this: HTMLMediaElement) {
    st(this).currentTime = 0;
    st(this).duration = this.getAttribute('src') ? 100 : NaN;
  };
});

const track = (id: string): AudioTrack => ({ id, title: `${id}.mp3`, url: `http://server/stream/${id}` });
const flush = () => new Promise((r) => setTimeout(r, 0));

function tick(el: HTMLAudioElement, time: number) {
  st(el).currentTime = time;
  el.dispatchEvent(new Event('timeupdate'));
}

let engine: AudioEngine;

async function startQueue(ids: string[]) {
  await engine.playQueue(ids.map(track), 0);
  const el = engine.audio;
  st(el).duration = 100;
  el.dispatchEvent(new Event('loadedmetadata'));
  return el;
}

const currentId = () => {
  const s = usePlayer.getState();
  return s.queue[s.index]?.id;
};

describe('AudioEngine track changes', () => {
  beforeEach(() => {
    holdPlaying = false;
    usePlayer.getState().clear();
    usePlayer.setState({ shuffle: false, originalOrder: null, repeat: 'off', crossfadeSeconds: 0, volume: 1, muted: false });
    engine = new AudioEngine();
  });

  afterEach(() => engine.dispose());

  it('plays the queue from the chosen track', async () => {
    await engine.playQueue(['a', 'b', 'c'].map(track), 1);
    expect(currentId()).toBe('b');
    expect(engine.audio.getAttribute('src')).toBe('http://server/stream/b');
    expect(usePlayer.getState().status).toBe('playing');
  });

  it('prepares the next track in the other element before the end', async () => {
    const a = await startQueue(['a', 'b']);
    const standby = engine.elements.find((e) => e !== a)!;
    tick(a, 50);
    expect(standby.getAttribute('src')).toBeNull(); // too early
    tick(a, 75);
    expect(standby.getAttribute('src')).toBe('http://server/stream/b');
  });

  it('starts the next track while the current one still sounds (screen-off safe)', async () => {
    const a = await startQueue(['a', 'b']);
    tick(a, 75);
    holdPlaying = true; // the new element has not produced sound yet
    tick(a, 99.8);
    await flush();

    const b = engine.audio;
    expect(b).not.toBe(a);
    expect(currentId()).toBe('b');
    // The old track keeps playing until the new one is really playing
    expect(a.paused).toBe(false);
    expect(engine.outgoing).toBe(a);
    expect(usePlayer.getState().status).not.toBe('paused');

    b.dispatchEvent(new Event('playing'));
    expect(a.paused).toBe(true);
    expect(a.getAttribute('src')).toBeNull();
    expect(engine.outgoing).toBeNull();
    expect(usePlayer.getState().status).toBe('playing');
  });

  it('still advances when the loop wraps before the next track was ready', async () => {
    const a = await startQueue(['a', 'b']);
    expect(a.loop).toBe(false);
    tick(a, 20); // more than 30 s left: not prepared
    await new Promise((r) => setTimeout(r, 2100)); // past the seek-suppress window
    // Pretend the next track could not be prepared in time
    (engine as unknown as { preparingFor: string | null }).preparingFor = usePlayer.getState().queue[1].qid;
    tick(a, 95);
    expect(a.loop).toBe(true); // end-guard: 'ended' can never fire
    tick(a, 0.2); // wrapped
    await flush();
    expect(currentId()).toBe('b');
  });

  it('does not loop the last track when nothing follows', async () => {
    const a = await startQueue(['a']);
    tick(a, 99);
    expect(a.loop).toBe(false);
  });

  it('repeat-one loops the element natively', async () => {
    usePlayer.setState({ repeat: 'one' });
    const a = await startQueue(['a', 'b']);
    tick(a, 99.9);
    expect(a.loop).toBe(true);
    expect(currentId()).toBe('a');
  });

  it('pauses at the end of the track with the "end of track" sleep timer', async () => {
    const a = await startQueue(['a', 'b']);
    engine.setSleepTimer('track');
    tick(a, 99.9);
    expect(a.paused).toBe(true);
    expect(currentId()).toBe('a');
    expect(usePlayer.getState().sleepAtEndOfTrack).toBe(false);
  });

  it('fades out and pauses when the sleep timer expires', async () => {
    const a = await startQueue(['a', 'b']);
    usePlayer.getState().setSleep(Date.now() + 5000, false);
    tick(a, 10);
    expect(a.volume).toBeGreaterThan(0.3);
    expect(a.volume).toBeLessThan(0.6);
    usePlayer.getState().setSleep(Date.now() - 1, false);
    tick(a, 11);
    expect(a.paused).toBe(true);
    expect(usePlayer.getState().sleepEndsAt).toBeNull();
  });

  it('crossfades: both play, the fade starts once the new track sounds', async () => {
    usePlayer.setState({ crossfadeSeconds: 5 });
    const a = await startQueue(['a', 'b']);
    tick(a, 70);
    holdPlaying = true;
    tick(a, 95.1); // inside the 5 s fade window
    await flush();
    const b = engine.audio;
    expect(b).not.toBe(a);
    expect(a.paused).toBe(false);
    expect(b.volume).toBe(0); // starts silent
    b.dispatchEvent(new Event('playing'));
    expect(engine.outgoing).toBe(a); // still fading out
  });

  it('a user skip switches at once (no overlap)', async () => {
    const a = await startQueue(['a', 'b']);
    await engine.next();
    expect(a.paused).toBe(true);
    expect(engine.outgoing).toBeNull();
    expect(currentId()).toBe('b');
  });

  it('previous restarts the track after 3 seconds, otherwise goes back', async () => {
    await startQueue(['a', 'b']);
    await engine.next();
    const b = engine.audio;
    st(b).duration = 100;
    st(b).currentTime = 20;
    await engine.previous();
    expect(currentId()).toBe('b');
    expect(b.currentTime).toBe(0);
    await engine.previous();
    expect(currentId()).toBe('a');
  });

  it('stops on the last track at the end of the queue', async () => {
    await startQueue(['a']);
    await engine.next({ auto: true });
    expect(currentId()).toBe('a');
    expect(usePlayer.getState().status).toBe('paused');
  });

  it('drops a prepared track when the queue changes under it', async () => {
    const a = await startQueue(['a', 'b', 'c']);
    tick(a, 75);
    const standby = engine.elements.find((e) => e !== a)!;
    expect(standby.getAttribute('src')).toBe('http://server/stream/b');
    engine.move(2, 1); // c now plays next
    expect(standby.getAttribute('src')).toBeNull();
    tick(a, 76);
    expect(standby.getAttribute('src')).toBe('http://server/stream/c');
  });

  it('removing the current track plays the following one', async () => {
    await startQueue(['a', 'b']);
    engine.remove(usePlayer.getState().queue[0].qid);
    await flush();
    expect(currentId()).toBe('b');
    expect(engine.audio.getAttribute('src')).toBe('http://server/stream/b');
  });

  it('resumes a restored session paused, at the saved position', async () => {
    usePlayer.getState().replaceQueue(['a', 'b'].map(track), 1);
    usePlayer.setState({ status: 'paused', savedPosition: 42 });
    expect(engine.audio.getAttribute('src')).toBeNull(); // nothing loads by itself
    engine.play();
    await flush();
    expect(engine.audio.getAttribute('src')).toBe('http://server/stream/b');
    st(engine.audio).duration = 100;
    engine.audio.dispatchEvent(new Event('loadedmetadata'));
    expect(engine.audio.currentTime).toBe(42);
  });

  it('applies volume and mute to the active element', async () => {
    const a = await startQueue(['a']);
    engine.setVolume(0.4);
    expect(a.volume).toBeCloseTo(0.4);
    engine.toggleMute();
    expect(a.volume).toBe(0);
    engine.toggleMute();
    expect(a.volume).toBeCloseTo(0.4);
  });
});
