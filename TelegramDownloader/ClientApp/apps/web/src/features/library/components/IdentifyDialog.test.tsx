import type { ApiLibraryFileDto } from '@tfm/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/lib/api';
import { SEARCH_DELAY_MS } from '../libraryModel';
import { IdentifyDialog, type IdentifyTarget } from './IdentifyDialog';

const dark = { provider: 'tmdb', providerId: '70523', kind: 'series', title: 'Dark', originalTitle: null, year: 2017, overview: null, posterUrl: null, imdbId: null };

const file = {
  channelId: 100,
  fileId: 'f1',
  fileName: '03 - Fantasmas.mkv',
  folderPath: '/Misc/Dark/Temporada 2/',
  kind: 'series',
  season: 2,
  episode: 3,
  parsed: { title: 'Fantasmas', year: null, season: 2, episode: 3, isSeries: true, source: 'file' },
  item: null
} as unknown as ApiLibraryFileDto;

function show(target: IdentifyTarget) {
  const onClose = vi.fn();
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <IdentifyDialog target={target} onClose={onClose} />
    </QueryClientProvider>
  );
  return onClose;
}

let search: ReturnType<typeof vi.spyOn>;
const queries = () => search.mock.calls.map((c: unknown[]) => (c[0] as { q: string }).q);

beforeEach(() => {
  search = vi.spyOn(api.library, 'searchProviders').mockResolvedValue([dark]) as never;
  vi.spyOn(api.library, 'folder').mockImplementation(async (channelId, path) => ({
    channelId: Number(channelId),
    path,
    videos: 18,
    locked: 2,
    rule: null,
    effectiveRule: null,
    itemId: null,
    scanStarted: null
  }));
  vi.spyOn(api.library, 'scanState').mockResolvedValue(null);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('IdentifyDialog', () => {
  it('does not search on every key', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    show({ type: 'file', file });
    const input = screen.getByRole('textbox', { name: 'Title' });
    await waitFor(() => expect(queries()).toEqual(['Fantasmas']));
    for (const text of ['D', 'Da', 'Dar', 'Dark']) {
      fireEvent.change(input, { target: { value: text } });
      await act(() => vi.advanceTimersByTimeAsync(SEARCH_DELAY_MS - 300));
    }
    expect(queries()).toEqual(['Fantasmas']);
    await act(() => vi.advanceTimersByTimeAsync(600));
    expect(queries()).toEqual(['Fantasmas', 'Dark']);
  });

  it('searches at once with Enter', async () => {
    show({ type: 'file', file });
    await waitFor(() => expect(queries()).toEqual(['Fantasmas']));
    const input = screen.getByRole('textbox', { name: 'Title' });
    fireEvent.change(input, { target: { value: 'Dark' } });
    fireEvent.submit(input);
    await waitFor(() => expect(queries()).toEqual(['Fantasmas', 'Dark']));
  });

  it('assigns the folder of the series, not the one of the season', async () => {
    const assign = vi.spyOn(api.library, 'assignFolder').mockResolvedValue({} as never);
    const onClose = show({ type: 'file', file });
    fireEvent.click(screen.getByText('The whole folder'));
    expect(await screen.findByText('18 videos.', { exact: false })).toBeInTheDocument();
    expect(screen.queryByText('Episode of this file')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Also the 2 files identified by hand' }));
    fireEvent.click(await screen.findByText('Dark'));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(assign).toHaveBeenCalledWith({ channelId: 100, path: '/Misc/Dark/', provider: 'tmdb', providerId: '70523', includeLocked: true });
  });

  it('assigns a folder of the file manager', async () => {
    const assign = vi.spyOn(api.library, 'assignFolder').mockResolvedValue({} as never);
    show({ type: 'folder', channelId: '100', path: '/Misc/Dark/', name: 'Dark' });
    expect(screen.queryByText('Movie')).not.toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: /Dark.*2017/ }));
    await waitFor(() => expect(assign).toHaveBeenCalledWith({ channelId: 100, path: '/Misc/Dark/', provider: 'tmdb', providerId: '70523', includeLocked: false }));
  });

  it('shows the title a folder has and removes it', async () => {
    const rule = { channelId: 100, path: '/Misc/Dark/', kind: 'series', provider: 'tmdb', providerId: '1', title: 'Otra', year: 2001 };
    vi.spyOn(api.library, 'folder').mockResolvedValue({ channelId: 100, path: '/Misc/Dark/', videos: 1, locked: 0, rule, effectiveRule: rule, itemId: null, scanStarted: null });
    const remove = vi.spyOn(api.library, 'unassignFolder').mockResolvedValue(undefined as never);
    show({ type: 'folder', channelId: 100, path: '/Misc/Dark/', name: 'Dark' });
    expect(await screen.findByText('Now: Otra (2001)')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    await waitFor(() => expect(remove).toHaveBeenCalledWith(100, '/Misc/Dark/'));
  });

  it('still identifies a single file', async () => {
    const match = vi.spyOn(api.library, 'matchFile').mockResolvedValue({} as never);
    show({ type: 'file', file });
    fireEvent.click(await screen.findByText('Dark'));
    await waitFor(() => expect(match).toHaveBeenCalledWith(100, 'f1', { provider: 'tmdb', providerId: '70523', kind: 'series', season: 2, episode: 3 }));
  });
});
