import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { KeyboardEvent, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { createClipboardStore } from '../core/clipboard';
import { createMemoryProvider } from '../providers/memoryProvider';
import { useFileManager, type UseFileManagerOptions } from './useFileManager';

function setup(options: Partial<UseFileManagerOptions> = {}) {
  const provider = createMemoryProvider({
    files: [
      { path: '/music/rock/a.mp3', size: 10 },
      { path: '/music/rock/b.mp3', size: 20 },
      { path: '/music/jazz/', folder: true },
      { path: '/music/c.flac', size: 30 },
      { path: '/photos/p.jpg', size: 5 },
      { path: '/readme.txt', size: 1 }
    ]
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const clipboard = createClipboardStore();
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  const hook = renderHook((props: Partial<UseFileManagerOptions>) => useFileManager({ provider, clipboard, searchDebounceMs: 0, confirmDelete: () => true, ...options, ...props }), {
    wrapper,
    initialProps: {}
  });
  return { provider, hook, clipboard };
}

const names = (h: ReturnType<typeof setup>['hook']) => h.result.current.entries.map((e) => e.name);
const key = (k: string, mods: Partial<KeyboardEvent> = {}) =>
  ({ key: k, target: document.body, preventDefault() {}, stopPropagation() {}, ...mods }) as unknown as KeyboardEvent<HTMLElement>;

describe('useFileManager', () => {
  it('lists the root (folders first) and navigates with history', async () => {
    const onPathChange = vi.fn();
    const { hook } = setup({ onPathChange });
    await waitFor(() => expect(names(hook)).toEqual(['music', 'photos', 'readme.txt']));
    act(() => hook.result.current.navigate('/music'));
    await waitFor(() => expect(names(hook)).toEqual(['jazz', 'rock', 'c.flac']));
    expect(onPathChange).toHaveBeenLastCalledWith('/music');
    expect(hook.result.current.breadcrumbs.map((b) => b.name)).toEqual(['Files', 'music']);
    act(() => hook.result.current.open(hook.result.current.entries.find((e) => e.name === 'rock')!));
    await waitFor(() => expect(hook.result.current.path).toBe('/music/rock'));
    act(() => hook.result.current.back());
    expect(hook.result.current.path).toBe('/music');
    expect(hook.result.current.canForward).toBe(true);
    act(() => hook.result.current.forward());
    expect(hook.result.current.path).toBe('/music/rock');
    act(() => hook.result.current.up());
    expect(hook.result.current.path).toBe('/music');
  });

  it('follows a controlled path', async () => {
    const { hook } = setup({ path: '/music' });
    await waitFor(() => expect(names(hook)).toContain('rock'));
    hook.rerender({ path: '/photos' });
    await waitFor(() => expect(names(hook)).toEqual(['p.jpg']));
    expect(hook.result.current.canBack).toBe(true);
  });

  it('selects with the keyboard and opens with Enter', async () => {
    const { hook } = setup();
    await waitFor(() => expect(names(hook)).toHaveLength(3));
    act(() => hook.result.current.handleKeyDown(key('ArrowDown')));
    act(() => hook.result.current.handleKeyDown(key('ArrowDown', { shiftKey: true })));
    expect(hook.result.current.selection.entries.map((e) => e.name)).toEqual(['music', 'photos']);
    act(() => hook.result.current.handleKeyDown(key('a', { ctrlKey: true })));
    expect(hook.result.current.selection.entries).toHaveLength(3);
    act(() => hook.result.current.handleKeyDown(key('Escape')));
    expect(hook.result.current.selection.entries).toHaveLength(0);
    act(() => hook.result.current.handleKeyDown(key('p')));
    expect(hook.result.current.selection.focusId).toBe('/photos');
    act(() => hook.result.current.handleKeyDown(key('Enter')));
    await waitFor(() => expect(names(hook)).toEqual(['p.jpg']));
    // The selection belongs to the folder it was made in
    expect(hook.result.current.selection.entries).toHaveLength(0);
    act(() => hook.result.current.handleKeyDown(key('Backspace')));
    expect(hook.result.current.path).toBe('/');
  });

  it('cuts and pastes into another folder (move)', async () => {
    const { hook, provider } = setup();
    await waitFor(() => expect(names(hook)).toHaveLength(3));
    act(() => hook.result.current.selection.dispatch({ type: 'click', id: '/readme.txt' }));
    act(() => hook.result.current.handleKeyDown(key('x', { ctrlKey: true })));
    expect(hook.result.current.clipboard.state?.mode).toBe('cut');
    expect(hook.result.current.clipboard.isCut('/readme.txt')).toBe(true);
    // Pasting in the same folder is blocked
    expect(hook.result.current.clipboard.blockReason).toBe('same-folder');
    act(() => hook.result.current.navigate('/photos'));
    await waitFor(() => expect(names(hook)).toEqual(['p.jpg']));
    expect(hook.result.current.clipboard.blockReason).toBeNull();
    await act(() => hook.result.current.clipboard.paste());
    await waitFor(() => expect(names(hook)).toEqual(['p.jpg', 'readme.txt']));
    expect(provider.snapshot().some((e) => e.path === '/readme.txt')).toBe(false);
    expect(hook.result.current.clipboard.state).toBeNull();
  });

  it('copies with Ctrl+C / Ctrl+V and keeps the clipboard', async () => {
    const { hook, provider } = setup({ path: '/music/rock' });
    await waitFor(() => expect(names(hook)).toHaveLength(2));
    act(() => hook.result.current.selection.dispatch({ type: 'selectAll' }));
    act(() => hook.result.current.handleKeyDown(key('c', { ctrlKey: true })));
    hook.rerender({ path: '/music/jazz' });
    await waitFor(() => expect(hook.result.current.path).toBe('/music/jazz'));
    await act(async () => {
      hook.result.current.handleKeyDown(key('v', { ctrlKey: true }));
    });
    await waitFor(() => expect(names(hook)).toEqual(['a.mp3', 'b.mp3']));
    expect(provider.snapshot().filter((e) => e.name === 'a.mp3')).toHaveLength(2);
    expect(hook.result.current.clipboard.state?.mode).toBe('copy');
  });

  it('renames with F2, creates folders and deletes after confirming', async () => {
    const confirmDelete = vi.fn(() => true);
    const onSuccess = vi.fn();
    const { hook } = setup({ confirmDelete, onSuccess });
    await waitFor(() => expect(names(hook)).toHaveLength(3));
    act(() => hook.result.current.selection.dispatch({ type: 'click', id: '/readme.txt' }));
    act(() => hook.result.current.handleKeyDown(key('F2')));
    expect(hook.result.current.rename.editingId).toBe('/readme.txt');
    await act(() => hook.result.current.rename.commit('notes.txt'));
    await waitFor(() => expect(names(hook)).toContain('notes.txt'));
    await act(() => hook.result.current.createFolder('new'));
    await waitFor(() => expect(names(hook)).toContain('new'));
    act(() => hook.result.current.selection.dispatch({ type: 'click', id: '/notes.txt' }));
    await act(async () => {
      hook.result.current.handleKeyDown(key('Delete'));
    });
    await waitFor(() => expect(names(hook)).not.toContain('notes.txt'));
    expect(confirmDelete).toHaveBeenCalledWith([expect.objectContaining({ name: 'notes.txt' })]);
    expect(onSuccess.mock.calls.map((c) => c[0])).toEqual(['rename', 'createFolder', 'delete']);
  });

  it('does not delete when the confirmation is refused', async () => {
    const { hook } = setup({ confirmDelete: () => false });
    await waitFor(() => expect(names(hook)).toHaveLength(3));
    await act(() => hook.result.current.deleteEntries([hook.result.current.entries[2]]));
    expect(names(hook)).toContain('readme.txt');
  });

  it('searches recursively (debounced) and filters by kind', async () => {
    const { hook } = setup();
    await waitFor(() => expect(names(hook)).toHaveLength(3));
    act(() => hook.result.current.setSearch('mp3'));
    await waitFor(() => expect(names(hook)).toEqual(['a.mp3', 'b.mp3']));
    expect(hook.result.current.isSearching).toBe(true);
    act(() => hook.result.current.setSearch(''));
    act(() => hook.result.current.navigate('/music'));
    act(() => hook.result.current.setFilter({ mediaKinds: ['audio'] }));
    await waitFor(() => expect(names(hook)).toEqual(['jazz', 'rock', 'c.flac']));
    act(() => hook.result.current.setFilter({ mediaKinds: ['image'] }));
    await waitFor(() => expect(names(hook)).toEqual(['jazz', 'rock']));
  });

  it('uploads through the queue and refreshes the listing', async () => {
    const { hook } = setup({ path: '/photos' });
    await waitFor(() => expect(names(hook)).toEqual(['p.jpg']));
    act(() => hook.result.current.uploads.add([new File(['hello'], 'new.png')]));
    await waitFor(() => expect(hook.result.current.uploads.items[0]?.status).toBe('done'));
    await waitFor(() => expect(names(hook)).toEqual(['new.png', 'p.jpg']));
  });

  it('moves entries dropped on a folder and calls onOpen for files', async () => {
    const onOpen = vi.fn(() => true);
    const { hook } = setup({ onOpen });
    await waitFor(() => expect(names(hook)).toHaveLength(3));
    const readme = hook.result.current.entries.find((e) => e.name === 'readme.txt')!;
    act(() => hook.result.current.open(readme));
    expect(onOpen).toHaveBeenCalledWith(readme, expect.anything());
    await act(() => hook.result.current.moveEntries([readme], '/music'));
    await waitFor(() => expect(names(hook)).toEqual(['music', 'photos']));
  });
});
