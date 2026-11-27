import { describe, expect, it } from 'vitest';
import { normalizeLocalPath, PlayTracker, playerKey, toPlayerTrack } from './sync';

describe('playerKey', () => {
  it('builds the server key of channel and local tracks', () => {
    expect(playerKey({ channelId: '-1001', fileId: 'abc' })).toBe('channel:-1001:abc');
    expect(playerKey({ localPath: '/music\\rock/a.mp3' })).toBe('local:music/rock/a.mp3');
    expect(playerKey({ channelId: '-1001' })).toBeNull();
    expect(playerKey({ localPath: '/' })).toBeNull();
    expect(playerKey({})).toBeNull();
  });

  it('normalises local paths like the server', () => {
    expect(normalizeLocalPath('  //a/b c.mp3 ')).toBe('a/b c.mp3');
  });
});

describe('toPlayerTrack', () => {
  const base = { id: 'x', title: 'Song', url: '/x', source: 'My channel', size: 10 };

  it('maps a channel track with the file tags', () => {
    expect(toPlayerTrack({ ...base, channelId: '1', fileId: '2' }, { title: 'Tagged', artist: 'Band' }, 200)).toEqual({
      fileName: 'Song',
      channelName: 'My channel',
      title: 'Tagged',
      artist: 'Band',
      album: null,
      duration: 200,
      fileSize: 10,
      channelId: '1',
      fileId: '2',
      isLocalFile: false,
      filePath: null
    });
  });

  it('maps a local track by path', () => {
    const track = toPlayerTrack({ ...base, localPath: 'music/a.flac' });
    expect(track).toMatchObject({ isLocalFile: true, filePath: 'music/a.flac', fileName: 'a.flac', channelId: null, duration: null });
  });

  it('skips tracks the server cannot identify', () => {
    expect(toPlayerTrack({ ...base, channelId: '1' })).toBeNull();
  });
});

describe('PlayTracker', () => {
  /** Plays `seconds` in 0.25 s ticks from `from`; returns how many times it asked to record. */
  function play(tracker: PlayTracker, qid: string, from: number, seconds: number, duration: number) {
    let records = 0;
    for (let p = from; p <= from + seconds; p += 0.25) if (tracker.update(qid, p, duration, true)) records++;
    return records;
  }

  it('records once after 30 s listened', () => {
    const tracker = new PlayTracker();
    tracker.update('a', 0, 300, true);
    expect(play(tracker, 'a', 0, 29, 300)).toBe(0);
    expect(play(tracker, 'a', 29.25, 60, 300)).toBe(1);
  });

  it('does not count seeks as listening', () => {
    const tracker = new PlayTracker();
    tracker.update('a', 0, 300, true);
    expect(tracker.update('a', 100, 300, true)).toBe(false);
    expect(tracker.update('a', 200, 300, true)).toBe(false);
    expect(play(tracker, 'a', 200, 20, 300)).toBe(0);
  });

  it('records short tracks at the end', () => {
    const tracker = new PlayTracker();
    tracker.update('a', 0, 12, true);
    expect(play(tracker, 'a', 0, 12, 12)).toBe(1);
  });

  it('does not record while paused', () => {
    const tracker = new PlayTracker();
    tracker.update('a', 0, 300, false);
    for (let p = 0; p < 60; p += 0.25) expect(tracker.update('a', p, 300, false)).toBe(false);
  });

  it('starts a new play on another queue entry and on repeat one', () => {
    const tracker = new PlayTracker();
    tracker.update('a', 0, 20, true);
    expect(play(tracker, 'a', 0, 20, 20)).toBe(1);
    // repeat one: same entry back at 0 after the end
    expect(play(tracker, 'a', 0, 20, 20)).toBe(1);
    tracker.update('b', 0, 20, true);
    expect(play(tracker, 'b', 0, 20, 20)).toBe(1);
  });

  it('knows when a play reached the end', () => {
    expect(PlayTracker.completed(199.5, 200)).toBe(true);
    expect(PlayTracker.completed(150, 200)).toBe(false);
    expect(PlayTracker.completed(10, 0)).toBe(false);
  });
});
