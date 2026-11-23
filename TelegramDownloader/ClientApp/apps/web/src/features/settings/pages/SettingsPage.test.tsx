import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConfirmHost } from '@tfm/ui';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { sampleConfig } from '../model/fixtures';
import { useSettingsDraft } from '../model/useSettingsForm';
import SettingsPage from './SettingsPage';

function envelope(data: unknown, status = 200) {
  return new Response(JSON.stringify({ success: status < 400, data, error: status < 400 ? null : { code: 'invalid_request', message: 'Bad value' } }), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

function mockServer(patch: (body: Record<string, unknown>) => Response) {
  const patches: Record<string, unknown>[] = [];
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const path = url.replace(/^https?:\/\/[^/]+/, '').split('?')[0];
    const method = init?.method ?? 'GET';
    if (path === '/api/v1/config' && method === 'GET') return envelope(sampleConfig());
    if (path === '/api/v1/config' && method === 'PATCH') {
      const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
      patches.push(body);
      return patch(body);
    }
    if (path === '/api/v1/auth/status') return envelope({ step: 'ok', isAuthenticated: true, isConfigured: true, user: { id: 1, username: 'me', firstName: 'Me', lastName: null, phone: null, isPremium: false } });
    return envelope(null, 404);
  });
  return patches;
}

function renderSettings(section = 'general') {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/settings', element: <SettingsPage /> },
      { path: '/settings/:section', element: <SettingsPage /> },
      { path: '/channels', element: <p>Channels page</p> }
    ],
    { initialEntries: [`/settings/${section}`] }
  );
  render(
    <QueryClientProvider client={qc}>
      <RouterProvider router={router} />
      <ConfirmHost />
    </QueryClientProvider>
  );
  return { router, qc };
}

afterEach(() => useSettingsDraft.getState().reset());

describe('settings page', { timeout: 30_000 }, () => {
  it('saves only the changed field and clears the unsaved bar', async () => {
    const user = userEvent.setup();
    const patches = mockServer((body) => envelope(sampleConfig(body as never)));
    renderSettings();
    const input = await screen.findByLabelText('Parallel chunk transfers');
    await user.clear(input);
    await user.type(input, '8');
    expect(await screen.findByText('1 unsaved changes')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(patches).toEqual([{ parallelTransfers: 8 }]));
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Unsaved changes' })).not.toBeInTheDocument());
  });

  it('blocks saving while a value is out of range', async () => {
    const user = userEvent.setup();
    const patches = mockServer(() => envelope(sampleConfig()));
    renderSettings();
    const input = await screen.findByLabelText('Max. simultaneous downloads');
    await user.clear(input);
    await user.type(input, '9');
    expect(await screen.findByText('Must be between 1 and 3')).toBeInTheDocument();
    expect(screen.getByText('Fix 1 invalid values to save.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    expect(patches).toEqual([]);
  });

  it('restores the draft when the server rejects the change', async () => {
    const user = userEvent.setup();
    mockServer(() => envelope(null, 400));
    renderSettings();
    const input = await screen.findByLabelText('Parallel chunk transfers');
    await user.clear(input);
    await user.type(input, '12');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(useSettingsDraft.getState().draft).toEqual({ parallelTransfers: 12 }));
    expect(screen.getByText('1 unsaved changes')).toBeInTheDocument();
  });

  it('asks before leaving the settings with unsaved changes', async () => {
    const user = userEvent.setup();
    mockServer(() => envelope(sampleConfig()));
    const { router } = renderSettings();
    const input = await screen.findByLabelText('Parallel chunk transfers');
    await user.clear(input);
    await user.type(input, '6');
    void router.navigate('/channels');
    expect(await screen.findByText('Discard the unsaved changes?')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Discard' }));
    await waitFor(() => expect(router.state.location.pathname).toBe('/channels'));
    expect(useSettingsDraft.getState().draft).toEqual({});
  });

  it('filters the settings with the search box', async () => {
    const user = userEvent.setup();
    mockServer(() => envelope(sampleConfig()));
    renderSettings();
    await user.type(screen.getByLabelText('Search settings...'), 'webdav pass');
    const results = screen.getByRole('list', { name: 'Search results' });
    expect(results).toHaveTextContent('WebDAV password');
    expect(results).not.toHaveTextContent('Parallel chunk transfers');
  });
});
