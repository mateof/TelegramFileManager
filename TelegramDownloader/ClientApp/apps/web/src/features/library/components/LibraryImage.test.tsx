import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { IMAGE_RETRY_MS, LibraryImage } from './LibraryImage';

const SRC = '/api/v1/library/images/abc?size=w342';

function mainImage(container: HTMLElement): HTMLImageElement | null {
  return container.querySelector('img:not([aria-hidden])');
}

function tinyImage(container: HTMLElement): HTMLImageElement | null {
  return container.querySelector('img[aria-hidden]');
}

describe('LibraryImage', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('asks the server cache for the wanted size, lazily and decoded off the main thread', () => {
    const { container } = render(<LibraryImage src={SRC} alt="Dune" size="w500" />);
    const img = mainImage(container)!;
    expect(img.getAttribute('src')).toBe('/api/v1/library/images/abc?size=w500');
    expect(img.getAttribute('alt')).toBe('Dune');
    expect(img.getAttribute('loading')).toBe('lazy');
    expect(img.getAttribute('decoding')).toBe('async');
    expect(img.getAttribute('srcset')).toBeNull();
    expect(tinyImage(container)!.getAttribute('src')).toBe('/api/v1/library/images/abc?size=w92');
  });

  it('offers a 2x size when given one, and loads eagerly above the fold', () => {
    const { container } = render(<LibraryImage src={SRC} alt="" size="w300" size2x="w780" eager />);
    const img = mainImage(container)!;
    expect(img.getAttribute('srcset')).toBe('/api/v1/library/images/abc?size=w300 1x, /api/v1/library/images/abc?size=w780 2x');
    expect(img.getAttribute('loading')).toBe('eager');
  });

  it('shows the placeholder when there is no image', () => {
    const { container } = render(<LibraryImage src={null} alt="" size="w342" />);
    expect(screen.getByTestId('library-image-placeholder')).toBeTruthy();
    expect(container.querySelector('img')).toBeNull();
  });

  it('retries once after a failure, then falls back to the placeholder', () => {
    const { container } = render(<LibraryImage src={SRC} alt="Dune" size="w342" />);
    fireEvent.error(mainImage(container)!);
    // While waiting the blurred tiny version stays
    expect(mainImage(container)).toBeNull();
    expect(tinyImage(container)).not.toBeNull();

    act(() => vi.advanceTimersByTime(IMAGE_RETRY_MS));
    const retried = mainImage(container)!;
    expect(retried.getAttribute('src')).toBe(SRC);

    fireEvent.error(retried);
    expect(screen.getByTestId('library-image-placeholder')).toBeTruthy();
    act(() => vi.advanceTimersByTime(IMAGE_RETRY_MS * 3));
    expect(mainImage(container)).toBeNull();
  });

  it('a retry that works shows the image', () => {
    const { container } = render(<LibraryImage src={SRC} alt="Dune" size="w342" />);
    fireEvent.error(mainImage(container)!);
    act(() => vi.advanceTimersByTime(IMAGE_RETRY_MS));
    const img = mainImage(container)!;
    fireEvent.load(img);
    expect(img.className).toContain('opacity-100');
    expect(screen.queryByTestId('library-image-placeholder')).toBeNull();
  });

  it('falls back to the placeholder at once when the tiny version failed too', () => {
    const { container } = render(<LibraryImage src={SRC} alt="" size="w342" />);
    fireEvent.error(tinyImage(container)!);
    fireEvent.error(mainImage(container)!);
    expect(screen.getByTestId('library-image-placeholder')).toBeTruthy();
  });
});
