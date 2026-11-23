import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { TooltipProvider } from '@tfm/ui';
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { createClipboardStore } from '../core/clipboard';
import { createMemoryProvider } from '../providers/memoryProvider';
import type { FileAction } from '../types';
import { FileManager } from './FileManager';

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

function renderFm(actions: FileAction[] = []) {
  const provider = createMemoryProvider({
    files: [{ path: '/docs/a.pdf', size: 2048 }, { path: '/song.mp3', size: 4096 }]
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <div style={{ height: 600 }}>
          <FileManager provider={provider} clipboard={createClipboardStore()} actions={actions} confirmDelete={() => true} />
        </div>
      </TooltipProvider>
    </QueryClientProvider>
  );
  return provider;
}

describe('<FileManager>', () => {
  it('renders the listing, selects on click and opens folders on double click', async () => {
    const run = vi.fn();
    renderFm([{ id: 'share', label: 'Share', selection: 'single', placement: ['selection'], run }]);
    const song = await screen.findByText('song.mp3');
    expect(screen.getByText('docs')).toBeTruthy();

    fireEvent.click(song);
    expect(await screen.findByText('1 selected')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Share' }));
    expect(run).toHaveBeenCalledWith([expect.objectContaining({ name: 'song.mp3' })], expect.anything());

    fireEvent.doubleClick(screen.getByText('docs'));
    expect(await screen.findByText('a.pdf')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Up one level' }));
    expect(await screen.findByText('song.mp3')).toBeTruthy();
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
