import type { ApiChatMessageDto, ChatSearchQuery, ChatWindowQuery } from '@tfm/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useViewer } from '@/features/player/viewerStore';
import { api } from '@/lib/api';
import { CHAT_SEARCH_DELAY_MS } from '../chatModel';
import ChatPage from './ChatPage';

function msg(id: number, extra: Partial<ApiChatMessageDto> = {}): ApiChatMessageDto {
  return { id, date: new Date(2026, 0, 1, 10, 0, id % 60).toISOString(), text: `message ${id}`, hasMedia: false, mediaType: null, fileName: null, fileSize: 0, mimeType: null, from: null, ...extra };
}
const doc = (id: number) => msg(id, { hasMedia: true, mediaType: 'document', fileName: `doc${id}.zip`, fileSize: 1024 });
const photo = (id: number) => msg(id, { hasMedia: true, mediaType: 'photo', mimeType: 'image/jpeg' });

let windows: ReturnType<typeof vi.spyOn>;
let searches: ReturnType<typeof vi.spyOn>;
const scrolledTo: string[] = [];

/** A chat with a message every 10 ids up to 1000; the multiples of 100 carry a document. */
const at = (id: number) => (id % 100 === 0 ? doc(id) : msg(id));
const windowOf = (from: number, to: number) => {
  const items: ApiChatMessageDto[] = [];
  for (let id = 1000; id > 0; id -= 10) if (id >= from && id <= to) items.push(at(id));
  return { items, hasOlder: from > 1, hasNewer: to < 1000, oldestId: items.at(-1)?.id ?? null, newestId: items[0]?.id ?? null };
};

function show(url: string) {
  const router = createMemoryRouter([{ path: '/chat/:id', element: <ChatPage /> }], { initialEntries: [url] });
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
  return router;
}

beforeEach(() => {
  scrolledTo.length = 0;
  Element.prototype.scrollIntoView = function (this: Element) {
    const id = this.getAttribute('data-message');
    if (id) scrolledTo.push(id);
  };
  windows = vi.spyOn(api.channels, 'messagesWindow').mockImplementation(async (_id, q: ChatWindowQuery = {}) =>
    q.around ? windowOf(q.around - 50, q.around + 50) : q.before ? windowOf(q.before - 100, q.before - 1) : q.after ? windowOf(q.after + 1, q.after + 100) : windowOf(891, 1000)
  ) as never;
  searches = vi.spyOn(api.channels, 'searchMessages').mockImplementation(async (_id, q: ChatSearchQuery) => {
    if (q.kind) return { items: q.kind === 'photos' ? [photo(700), photo(600)] : q.kind === 'documents' ? [doc(400), doc(300)] : [], total: 2, nextOffsetId: null };
    return q.offsetId ? { items: [at(100)], total: 3, nextOffsetId: null } : { items: [at(950), at(500)], total: 3, nextOffsetId: 500 };
  }) as never;
  vi.spyOn(api.channels, 'messageCounts').mockResolvedValue({ photos: 2, documents: 2, videos: 0 });
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  useViewer.setState({ open: false, items: [] });
});

describe('ChatPage', { timeout: 20_000 }, () => {
  it('opens at the message of the link', async () => {
    show('/chat/1?message=500');
    await screen.findByText('message 510');
    expect(windows.mock.calls[0]![1]).toMatchObject({ around: 500 });
    expect(scrolledTo).toEqual(['500']);
  });

  it('shows the neighbours of a deleted message', async () => {
    windows.mockResolvedValue({ items: [msg(510), msg(498)], hasOlder: false, hasNewer: false, oldestId: 498, newestId: 510 } as never);
    show('/chat/1?message=500');
    await screen.findByText('message 498');
    expect(scrolledTo).toEqual(['498']);
  });

  it('searches the whole chat and goes from result to result', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const router = show('/chat/1');
    await screen.findByText('message 950');
    fireEvent.click(screen.getByRole('button', { name: 'Search in this chat' }));
    const input = screen.getByRole('textbox', { name: 'Search in this chat' });
    for (const value of ['m', 'me', 'message']) fireEvent.change(input, { target: { value } });
    await act(() => vi.advanceTimersByTimeAsync(CHAT_SEARCH_DELAY_MS - 200));
    expect(searches).not.toHaveBeenCalled();
    fireEvent.submit(input);
    // The newest result is in the loaded part
    await screen.findByText('1 / 3');
    expect(searches).toHaveBeenCalledTimes(1);
    expect(scrolledTo.at(-1)).toBe('950');
    expect(windows).toHaveBeenCalledTimes(1);

    // The next one is far away: the part around it is loaded
    fireEvent.click(screen.getByRole('button', { name: 'Previous result (older)' }));
    await screen.findByText('2 / 3');
    await waitFor(() => expect(scrolledTo.at(-1)).toBe('500'));
    expect(windows.mock.calls.at(-1)![1]).toMatchObject({ around: 500 });
    expect(router.state.location.search).toBe('?message=500');

    // The last one comes in another page of results
    fireEvent.click(screen.getByRole('button', { name: 'Previous result (older)' }));
    await screen.findByText('3 / 3');
    await waitFor(() => expect(scrolledTo.at(-1)).toBe('100'));
    expect(searches.mock.calls.at(-1)![1]).toMatchObject({ q: 'message', offsetId: 500 });
    expect((screen.getByRole('button', { name: 'Previous result (older)' }) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: 'Next result (newer)' }));
    await screen.findByText('2 / 3');
    await waitFor(() => expect(scrolledTo.at(-1)).toBe('500'));
  });

  it('downloads the chosen messages', async () => {
    const download = vi.spyOn(api.transfers, 'downloadMessages').mockResolvedValue({ accepted: 2, skipped: [] } as never);
    vi.spyOn(api.local, 'list').mockResolvedValue({ contents: { items: [] }, page: { page: 1, pageSize: 1, totalItems: 0, hasNext: false } } as never);
    show('/chat/1');
    await screen.findByText('doc1000.zip');
    fireEvent.click(screen.getByRole('button', { name: 'Select files' }));
    // Only the messages with a file can be chosen
    expect(screen.getAllByRole('checkbox', { name: 'Select' })).toHaveLength(2);
    fireEvent.click(screen.getByText('doc1000.zip'));
    fireEvent.click(screen.getByText('doc900.zip'));
    expect(screen.getByText(/2 selected/)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Download to server' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Download' }));
    await waitFor(() => expect(download).toHaveBeenCalledWith({ chatId: 1, messageIds: [1000, 900], targetPath: '/' }));
    await waitFor(() => expect(screen.queryByText(/2 selected/)).toBeNull());
  });

  it('shows the media by kind and goes to the message of one', async () => {
    const router = show('/chat/1?view=media');
    const tabs = await screen.findAllByRole('tab');
    await waitFor(() => expect(screen.getAllByRole('tab').map((x) => x.textContent)).toEqual(['Photos2', 'Documents2']));
    expect(tabs.length).toBeGreaterThan(0);
    expect(windows).not.toHaveBeenCalled();
    await waitFor(() => expect(searches.mock.calls[0]![1]).toMatchObject({ kind: 'photos' }));

    // Photos open as a gallery, with a way to the message
    fireEvent.click((await screen.findAllByRole('button', { name: 'View photo' }))[1]!);
    await waitFor(() => expect(useViewer.getState().items).toHaveLength(2));
    expect(useViewer.getState().index).toBe(1);
    expect(useViewer.getState().items[1]!.goTo?.label).toBe('Go to the message');

    fireEvent.mouseDown(screen.getByRole('tab', { name: /Documents/ }), { button: 0 });
    await screen.findByText('doc400.zip');
    fireEvent.click(screen.getAllByRole('button', { name: 'Go to the message' })[1]!);
    await waitFor(() => expect(scrolledTo).toEqual(['300']));
    expect(router.state.location.search).toBe('?message=300');

    // Back returns to the media
    await act(() => router.navigate(-1));
    await screen.findByText('doc400.zip');
  });
});
