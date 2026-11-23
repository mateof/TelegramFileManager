import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import SetupPage from './SetupPage';

function envelope(data: unknown, status = 200, error?: { code: string; message: string; detail?: string }) {
  return new Response(JSON.stringify({ success: status < 400, data, error: error ?? null }), { status, headers: { 'Content-Type': 'application/json' } });
}

type Handler = (body: unknown) => Response;

/** fetch mock keyed by "METHOD /path"; records every call with its JSON body. */
function mockServer(routes: Record<string, Handler>) {
  const calls: { key: string; body: unknown }[] = [];
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const path = url.replace(/^https?:\/\/[^/]+/, '').split('?')[0];
    const key = `${init?.method ?? 'GET'} ${path}`;
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
    calls.push({ key, body });
    const handler = routes[key];
    return handler ? handler(body) : envelope(null, 404, { code: 'not_found', message: 'Not found' });
  });
  return calls;
}

function renderWizard() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/setup', element: <SetupPage /> },
      { path: '/', element: <p>Telegram login</p> }
    ],
    { initialEntries: ['/setup'] }
  );
  render(
    <QueryClientProvider client={qc}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
  return router;
}

const freshStatus = { currentStep: 'MongoDbRequired', mongoDbConfigured: false, mongoDbConnected: false, telegramConfigured: false, mongoDbError: null };
const webAuthOpen = { passwordSet: false, authenticated: true, lanBypassEnabled: false, lanBypassActive: true };

describe('setup wizard', { timeout: 30_000 }, () => {
  it('goes MongoDB → Telegram → web password → done, then to the Telegram login', async () => {
    const user = userEvent.setup();
    const calls = mockServer({
      'GET /api/v1/system/setup': () => envelope(freshStatus),
      'GET /api/v1/web-auth/status': () => envelope(webAuthOpen),
      'POST /api/v1/system/setup/test-mongo': () => envelope({ success: true, error: null }),
      'POST /api/v1/system/setup/test-telegram': () => envelope({ success: true, error: null }),
      'POST /api/v1/system/setup': () => envelope({ ...freshStatus, currentStep: 'TelegramRequired', mongoDbConnected: true }),
      'POST /api/v1/system/setup/complete': () => envelope(null, 409, { code: 'conflict', message: 'Setup is already complete' }),
      'POST /api/v1/web-auth/setup': () => envelope(null)
    });
    const router = renderWizard();

    // 1. MongoDB
    const conn = await screen.findByLabelText('Connection string');
    await user.clear(conn);
    await user.type(conn, 'mongodb://mongo:27017');
    await user.click(screen.getByRole('button', { name: /test & continue/i }));

    // 2. Telegram (409 on complete counts as success)
    await user.type(await screen.findByLabelText('API ID'), '123456');
    await user.type(screen.getByLabelText('API Hash'), '0123456789abcdef0123456789abcdef');
    await user.click(screen.getByRole('button', { name: /save & continue/i }));

    // 3. Optional web password
    expect(await screen.findByText('Protect the web interface')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Password'), 'supersecret');
    await user.type(screen.getByLabelText('Repeat the password'), 'supersecret');
    await user.click(screen.getByRole('button', { name: /set password/i }));

    // 4. Done
    expect(await screen.findByText('Setup complete!')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /continue to the telegram login/i }));
    await waitFor(() => expect(router.state.location.pathname).toBe('/'));

    const posts = calls.filter((c) => c.key.startsWith('POST'));
    expect(posts.map((c) => c.key)).toEqual([
      'POST /api/v1/system/setup/test-mongo',
      'POST /api/v1/system/setup',
      'POST /api/v1/system/setup/test-telegram',
      'POST /api/v1/system/setup',
      'POST /api/v1/system/setup/complete',
      'POST /api/v1/web-auth/setup'
    ]);
    expect(posts[1]!.body).toEqual({ mongoConnectionString: 'mongodb://mongo:27017' });
    expect(posts[3]!.body).toEqual({ apiId: '123456', apiHash: '0123456789abcdef0123456789abcdef' });
    expect(posts[5]!.body).toEqual({ password: 'supersecret' });
  });

  it('shows the error of a failed MongoDB test and does not save', async () => {
    const user = userEvent.setup();
    const calls = mockServer({
      'GET /api/v1/system/setup': () => envelope(freshStatus),
      'GET /api/v1/web-auth/status': () => envelope(webAuthOpen),
      'POST /api/v1/system/setup/test-mongo': () => envelope({ success: false, error: 'No server reachable' })
    });
    renderWizard();
    const conn = await screen.findByLabelText('Connection string');
    await user.clear(conn);
    await user.type(conn, 'mongodb://nowhere:27017');
    await user.click(screen.getByRole('button', { name: /test & continue/i }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No server reachable');
    expect(calls.some((c) => c.key === 'POST /api/v1/system/setup')).toBe(false);
  });

  it('validates the fields before calling the server', async () => {
    const user = userEvent.setup();
    const calls = mockServer({
      'GET /api/v1/system/setup': () => envelope({ ...freshStatus, currentStep: 'TelegramRequired', mongoDbConfigured: true, mongoDbConnected: true }),
      'GET /api/v1/web-auth/status': () => envelope(webAuthOpen)
    });
    renderWizard();
    // Mongo already done: the wizard starts at the Telegram step
    await user.type(await screen.findByLabelText('API ID'), 'abc');
    await user.type(screen.getByLabelText('API Hash'), 'short');
    await user.click(screen.getByRole('button', { name: /save & continue/i }));
    expect(screen.getByText('The api_id is a number')).toBeInTheDocument();
    expect(screen.getByText('The api_hash has 32 hexadecimal characters')).toBeInTheDocument();
    expect(calls.filter((c) => c.key.startsWith('POST'))).toEqual([]);
  });

  it('skips the password step when a password already exists', async () => {
    const user = userEvent.setup();
    mockServer({
      'GET /api/v1/system/setup': () => envelope({ ...freshStatus, currentStep: 'TelegramRequired', mongoDbConfigured: true, mongoDbConnected: true }),
      'GET /api/v1/web-auth/status': () => envelope({ ...webAuthOpen, passwordSet: true }),
      'POST /api/v1/system/setup/test-telegram': () => envelope({ success: true, error: null }),
      'POST /api/v1/system/setup': () => envelope({ ...freshStatus, currentStep: 'Complete' }),
      'POST /api/v1/system/setup/complete': () => envelope({ ...freshStatus, currentStep: 'Complete' })
    });
    renderWizard();
    await user.type(await screen.findByLabelText('API ID'), '123456');
    await user.type(screen.getByLabelText('API Hash'), '0123456789abcdef0123456789abcdef');
    await user.click(screen.getByRole('button', { name: /save & continue/i }));
    expect(await screen.findByText('Setup complete!')).toBeInTheDocument();
  });
});
