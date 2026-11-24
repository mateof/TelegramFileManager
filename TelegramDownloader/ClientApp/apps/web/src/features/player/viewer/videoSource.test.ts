import { describe, expect, it } from 'vitest';
import type { ViewerItem } from '../public';
import { fallbackVideoSource, pickVideoSource } from './videoSource';

const item = (name: string, url: string, playbackUrl?: string): ViewerItem => ({ id: name, name, kind: 'video', url, playbackUrl });
const plays = () => 'probably';

describe('video sources', () => {
  it('plays native containers directly and the others through the transcoder', () => {
    expect(pickVideoSource(item('a.mp4', '/c', '/c'), plays)).toBe('/c');
    expect(pickVideoSource(item('a.mkv', '/c', '/s'), plays)).toBe('/s');
    expect(pickVideoSource(item('a.mov', '/c', '/s'), plays)).toBe('/c');
  });

  it('falls back from the transcoder to the direct stream (transcoding off or failing)', () => {
    const mkv = item('a.mkv', '/c', '/s');
    // Used to give up here: a 403 from a disabled transcoder ended playback.
    expect(fallbackVideoSource(mkv, '/s')).toBe('/c');
    expect(fallbackVideoSource(mkv, '/c', ['/s'])).toBeNull();
  });

  it('falls back from the direct stream to the transcoder once', () => {
    const mov = item('a.mov', '/c', '/s');
    expect(fallbackVideoSource(mov, '/c')).toBe('/s');
    expect(fallbackVideoSource(mov, '/s', ['/c'])).toBeNull();
    expect(fallbackVideoSource(item('a.mp4', '/c', '/c'), '/c')).toBeNull();
    expect(fallbackVideoSource(item('a.mp4', '/c'), '/c')).toBeNull();
  });
});
