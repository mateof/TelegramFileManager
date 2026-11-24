import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ViewerItem } from '../public';
import VideoView from './VideoView';

const item = { id: 'v1', name: 'Holiday.mp4', kind: 'video', url: 'http://tfm.test/api/v1/files/v1.mp4' } as ViewerItem;

describe('VideoView', () => {
  /** Whether the element was in the page each time a source was given to it. */
  let connected: boolean[];

  beforeEach(() => {
    connected = [];
    vi.spyOn(HTMLMediaElement.prototype, 'src', 'set').mockImplementation(function (this: HTMLMediaElement) {
      connected.push(this.isConnected);
    });
    vi.spyOn(HTMLMediaElement.prototype, 'canPlayType').mockReturnValue('maybe');
  });
  afterEach(() => vi.restoreAllMocks());

  it('gives the video its source only once it is in the page, so no media event is lost', () => {
    const { container } = render(<VideoView item={item} />);
    // Not rendered as a prop: React would set it on the detached element
    expect(container.querySelector('video')).not.toHaveAttribute('src');
    expect(connected).toEqual([true]);
  });

  it('reads the duration and resumes when the metadata arrives', () => {
    const watched = { ...item, watch: { channelId: 'c', fileId: 'f', startAtSeconds: 42 } } as unknown as ViewerItem;
    const { container } = render(<VideoView item={watched} />);
    const video = container.querySelector('video')!;
    Object.defineProperty(video, 'duration', { value: 252, configurable: true });
    fireEvent.loadedMetadata(video);
    expect(video.currentTime).toBe(42);
    expect(screen.getByText(/4:12/)).toBeInTheDocument();
  });
});
