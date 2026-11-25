import type { ApiLibraryScanStateDto, AppConfigDto } from '@tfm/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConfirmHost } from '@tfm/ui';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/lib/api';
import { sampleConfig } from '../model/fixtures';
import { useSettingsDraft } from '../model/useSettingsForm';
import SettingsPage from '../pages/SettingsPage';

const rules = [
  { channelId: 1, path: '/Cine/', kind: 'movie' },
  { channelId: 1, path: '/Series/', kind: 'series' },
  { channelId: 2, path: '/', kind: 'ignore' }
] as AppConfigDto['libraryFolderRules'];

let scan: ReturnType<typeof vi.spyOn>;
let update: ReturnType<typeof vi.spyOn>;
let state: ApiLibraryScanStateDto | null;

beforeEach(() => {
  state = null;
  vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(JSON.stringify({ success: false, data: null, error: { code: 'not_found', message: '' } }), { status: 404 }));
  vi.spyOn(api.config, 'get').mockImplementation(async () => sampleConfig({ libraryEnabled: true, libraryFolderRules: rules }));
  update = vi.spyOn(api.config, 'update').mockImplementation(async () => sampleConfig({ libraryEnabled: true, libraryFolderRules: rules })) as never;
  vi.spyOn(api.library, 'scanState').mockImplementation(async () => state);
  scan = vi.spyOn(api.library, 'scan').mockResolvedValue(undefined as never) as never;
});

afterEach(() => {
  useSettingsDraft.getState().reset();
  vi.restoreAllMocks();
});

async function show() {
  const router = createMemoryRouter([{ path: '/settings/:section', element: <SettingsPage /> }], { initialEntries: ['/settings/library'] });
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <RouterProvider router={router} />
      <ConfirmHost />
    </QueryClientProvider>
  );
  await screen.findByText('/Cine/');
  return userEvent.setup();
}

describe('scan of folder rules', { timeout: 30_000 }, () => {
  it('scans one rule', async () => {
    const user = await show();
    await user.click(screen.getAllByRole('button', { name: 'Scan this folder' })[1]!);
    await waitFor(() => expect(scan).toHaveBeenCalledWith({ folders: [{ channelId: 1, path: '/Series/' }], force: false }));
    expect(update).not.toHaveBeenCalled();
  });

  it('scans the selected rules, also in full', async () => {
    const user = await show();
    await user.click(screen.getByRole('checkbox', { name: 'Select Channel 1 /Cine/' }));
    await user.click(screen.getByRole('checkbox', { name: 'Select Channel 2 /' }));
    expect(screen.getByText('2 folders selected')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Scan the selected' }));
    const folders = [
      { channelId: 1, path: '/Cine/' },
      { channelId: 2, path: '/' }
    ];
    await waitFor(() => expect(scan).toHaveBeenLastCalledWith({ folders, force: false }));
    await user.click(screen.getAllByRole('button', { name: 'Full rescan' })[0]!);
    await waitFor(() => expect(scan).toHaveBeenLastCalledWith({ folders, force: true }));
    // A rule that is removed is not scanned
    await user.click(screen.getAllByRole('button', { name: 'Remove' })[0]!);
    expect(screen.getByText('1 folder selected')).toBeInTheDocument();
  });

  it('saves the changes before scanning', async () => {
    const user = await show();
    await user.click(screen.getAllByRole('button', { name: 'Remove' })[0]!);
    await user.click(screen.getAllByRole('button', { name: 'Save & scan this folder' })[0]!);
    await waitFor(() => expect(scan).toHaveBeenCalledWith({ folders: [{ channelId: 1, path: '/Series/' }], force: false }));
    expect(update.mock.invocationCallOrder[0]!).toBeLessThan(scan.mock.invocationCallOrder[0]!);
  });

  it('waits for the running scan and says what it covers', async () => {
    state = { running: true, channelsTotal: 1, channelsScanned: 0, filesSeen: 0, scope: '1', folders: [{ channelId: 1, path: '/Cine/' }] } as ApiLibraryScanStateDto;
    await show();
    expect(await screen.findByText('A scan is running. Its progress is below.')).toBeInTheDocument();
    expect(screen.getByText('Only: Channel 1 /Cine/')).toBeInTheDocument();
    for (const button of screen.getAllByRole('button', { name: 'Scan this folder' })) expect(button).toBeDisabled();
  });
});
