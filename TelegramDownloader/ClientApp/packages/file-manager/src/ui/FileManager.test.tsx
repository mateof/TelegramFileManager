import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { TooltipProvider } from '@tfm/ui';
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { createClipboardStore } from '../core/clipboard';
import { createMemoryProvider } from '../providers/memoryProvider';
import type { FileAction } from '../types';
import { FileManager, type FileManagerViewProps } from './FileManager';

// jsdom has no layout: give elements a size so the virtualizer renders rows
const sizes = { offsetHeight: 600, offsetWidth: 800, clientWidth: 800, clientHeight: 600 };
const originals = new Map<string, PropertyDescriptor | undefined>();
beforeAll(() => {
  for (const [k, v] of Object.entries(sizes)) {
    originals.set(k, Object.getOwnPropertyDescriptor(HTMLElement.prototype, k));
    Object.defineProperty(HTMLElement.prototype, k, { configurable: true, get: () => v });
  }
});
afterAll(() => {
  for (const [k, d] of originals) {
    if (d) Object.defineProperty(HTMLElement.prototype, k, d);
    else Reflect.deleteProperty(HTMLElement.prototype, k);
  }
});

function renderFm(actions: FileAction[] = [], view: Partial<FileManagerViewProps> = {}) {
  const provider = createMemoryProvider({
    files: [{ path: '/docs/a.pdf', size: 2048 }, { path: '/song.mp3', size: 4096 }]
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <div style={{ height: 600 }}>
          <FileManager provider={provider} clipboard={createClipboardStore()} actions={actions} confirmDelete={() => true} {...view} />
        </div>
      </TooltipProvider>
    </QueryClientProvider>
  );
  return provider;
}

describe('<FileManager>', () => {
  it('renders the listing, selects files on click and opens folders on a single click', async () => {
    const run = vi.fn();
    renderFm([{ id: 'share', label: 'Share', selection: 'single', placement: ['selection'], run }]);
    const song = await screen.findByText('song.mp3');
    expect(screen.getByText('docs')).toBeTruthy();

    fireEvent.click(song);
    expect(await screen.findByText('1 selected')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Share' }));
    expect(run).toHaveBeenCalledWith([expect.objectContaining({ name: 'song.mp3' })], expect.anything());

    fireEvent.click(screen.getByText('docs'));
    expect(await screen.findByText('a.pdf')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Up one level' }));
    expect(await screen.findByText('song.mp3')).toBeTruthy();
  });

  it('selects folders with Ctrl/Shift+click instead of opening them', async () => {
    renderFm();
    await screen.findByText('song.mp3');
    fireEvent.click(screen.getByText('docs'), { ctrlKey: true });
    expect(await screen.findByText('1 selected')).toBeTruthy();
    expect(screen.queryByText('a.pdf')).toBeNull();
    fireEvent.click(screen.getByText('song.mp3'), { shiftKey: true });
    expect(await screen.findByText('2 selected')).toBeTruthy();
    expect(screen.queryByText('a.pdf')).toBeNull();
  });

  it('swallows the rest of a double click on a folder', async () => {
    const onOpen = vi.fn();
    const provider = createMemoryProvider({ files: [{ path: '/docs/a.pdf', size: 1 }] });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <TooltipProvider>
          <FileManager provider={provider} clipboard={createClipboardStore()} onOpen={onOpen} />
        </TooltipProvider>
      </QueryClientProvider>
    );
    fireEvent.click(await screen.findByText('docs'), { detail: 1 });
    const pdf = await screen.findByText('a.pdf');
    // The second click and the dblclick land on the entry now under the pointer
    fireEvent.click(pdf, { detail: 2 });
    fireEvent.doubleClick(pdf, { detail: 2 });
    expect(onOpen).not.toHaveBeenCalled();
    expect(screen.queryByText('1 selected')).toBeNull();
  });

  it('opens the entry of the first click when the list moves under a double click', async () => {
    const onOpen = vi.fn();
    const provider = createMemoryProvider({ files: [{ path: '/docs/a.pdf', size: 1 }, { path: '/song.mp3', size: 1 }] });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <TooltipProvider>
          <FileManager provider={provider} clipboard={createClipboardStore()} onOpen={onOpen} />
        </TooltipProvider>
      </QueryClientProvider>
    );
    fireEvent.click(await screen.findByText('song.mp3'), { detail: 1 });
    // The selection bar appeared and pushed the rows: the rest of the double click hits "docs"
    fireEvent.click(screen.getByText('docs'), { detail: 2 });
    fireEvent.doubleClick(screen.getByText('docs'), { detail: 2 });
    expect(onOpen).toHaveBeenCalledWith(expect.objectContaining({ name: 'song.mp3' }), expect.anything());
    expect(screen.queryByText('a.pdf')).toBeNull();
  });

  it('keeps double click to open folders when single click is off', async () => {
    renderFm([], { singleClickFolders: false });
    await screen.findByText('song.mp3');
    fireEvent.click(screen.getByText('docs'));
    expect(await screen.findByText('1 selected')).toBeTruthy();
    expect(screen.queryByText('a.pdf')).toBeNull();
    fireEvent.doubleClick(screen.getByText('docs'));
    expect(await screen.findByText('a.pdf')).toBeTruthy();
  });

  it('creates a folder from the toolbar', async () => {
    const provider = renderFm();
    await screen.findByText('song.mp3');
    fireEvent.click(screen.getByRole('button', { name: 'New folder' }));
    fireEvent.change(await screen.findByLabelText('Folder name'), { target: { value: 'Mixes' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));
    expect(await screen.findByText('Mixes')).toBeTruthy();
    expect(provider.snapshot().some((e) => e.path === '/Mixes')).toBe(true);
  });

  it('filters the folder from the search box', async () => {
    renderFm();
    await screen.findByText('song.mp3');
    fireEvent.change(screen.getByLabelText('Search'), { target: { value: 'a.pdf' } });
    await waitFor(() => expect(screen.queryByText('song.mp3')).toBeNull());
    expect(screen.getByText('a.pdf')).toBeTruthy();
  });
});
