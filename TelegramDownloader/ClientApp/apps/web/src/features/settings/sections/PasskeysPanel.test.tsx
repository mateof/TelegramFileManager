import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConfirmHost } from '@tfm/ui';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fakeCredential, installWebAuthn } from '@/test/webauthnMock';
import { suggestPasskeyName } from '../model/passkeys';
import { SecuritySection } from './SecuritySection';

function envelope(data: unknown, status = 200, code = 'x') {
  return new Response(JSON.stringify({ success: status < 400, data, error: status < 400 ? null : { code, message: code } }), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

const passkey = {
  id: 'cred-1',
  name: 'Bitwarden',
  createdUtc: new Date(Date.now() - 86_400_000).toISOString(),
  lastUsedUtc: null,
  backupEligible: true,
  backedUp: true,
  transports: ['hybrid'],
  aaGuid: '00000000-0000-0000-0000-000000000000',
  rpId: 'localhost'
};

interface Call {
  path: string;
  method: string;
  body: Record<string, unknown> | null;
}

function mockServer(options: { items?: unknown[]; list?: () => Response; registerFinish?: () => Response; passkeyCount?: number } = {}) {
  const calls: Call[] = [];
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const path = url.replace(/^https?:\/\/[^/]+/, '').split('?')[0];
    const method = init?.method ?? 'GET';
    calls.push({ path, method, body: init?.body ? (JSON.parse(String(init.body)) as Record<string, unknown>) : null });
    if (path === '/api/v1/web-auth/status')
      return envelope({ passwordSet: true, authenticated: true, lanBypassEnabled: false, lanBypassActive: false, passkeyCount: options.passkeyCount ?? 1, passkeysAvailable: true });
    if (path === '/api/v1/web-auth/passkeys' && method === 'GET')
      return options.list?.() ?? envelope({ items: options.items ?? [passkey], rpId: 'localhost', origins: ['http://localhost:5000'], supported: true, rpIdOverride: null, originsOverride: [] });
    if (path === '/api/v1/web-auth/passkeys/register/begin')
      return envelope({ requestId: 'r1', options: { challenge: 'YQ', rp: { id: 'localhost', name: 'TFM' }, user: { id: 'dQ', name: 'TFM', displayName: 'TFM' }, pubKeyCredParams: [] } });
    if (path === '/api/v1/web-auth/passkeys/register/finish') return options.registerFinish?.() ?? envelope({ ...passkey, id: 'cred-2', name: 'Phone' });
    if (path.startsWith('/api/v1/web-auth/passkeys/') && method === 'DELETE') return envelope(null);
    if (path.startsWith('/api/v1/web-auth/passkeys/') && method === 'PUT') return envelope({ ...passkey, name: 'Renamed' });
    return envelope(null, 404);
  });
  return calls;
}

function renderSection() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <SecuritySection />
      </MemoryRouter>
      <ConfirmHost />
    </QueryClientProvider>
  );
}

let webauthn: ReturnType<typeof installWebAuthn> | null = null;
afterEach(() => {
  webauthn?.restore();
  webauthn = null;
});

describe('passkeys settings', { timeout: 30_000 }, () => {
  it('lists passkeys with the synced badge and the domain note', async () => {
    webauthn = installWebAuthn();
    mockServer();
    renderSection();
    const list = await screen.findByRole('list', { name: 'Passkeys' });
    expect(within(list).getByText('Bitwarden')).toBeInTheDocument();
    expect(within(list).getByText('Synced')).toBeInTheDocument();
    expect(within(list).getByText(/Never used/)).toBeInTheDocument();
    expect(screen.getByText(/A passkey only works on the domain where it was created/)).toBeInTheDocument();
    expect(screen.getByText('localhost')).toBeInTheDocument();
  });

  it('adds a passkey with the current password', async () => {
    const user = userEvent.setup();
    webauthn = installWebAuthn();
    webauthn.create.mockResolvedValue(fakeCredential('create', 'cred-2'));
    const calls = mockServer();
    renderSection();

    await user.click(await screen.findByRole('button', { name: 'Add passkey' }));
    const dialog = await screen.findByRole('dialog');
    const name = within(dialog).getByLabelText('Name');
    await user.clear(name);
    await user.type(name, 'Phone');
    await user.type(within(dialog).getByLabelText('Current password'), 'my-password');
    await user.click(within(dialog).getByRole('button', { name: 'Create passkey' }));

    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/register/finish'))).toBe(true));
    expect(calls.find((c) => c.path.endsWith('/register/begin'))!.body).toEqual({ currentPassword: 'my-password' });
    const finish = calls.find((c) => c.path.endsWith('/register/finish'))!.body!;
    expect(finish).toMatchObject({ requestId: 'r1', name: 'Phone' });
    expect((finish.credential as Record<string, unknown>).id).toBe('cred-2');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('explains an authenticator that already has a passkey, and a wrong password', async () => {
    const user = userEvent.setup();
    webauthn = installWebAuthn();
    webauthn.create.mockRejectedValue(new DOMException('exists', 'InvalidStateError'));
    mockServer();
    renderSection();

    await user.click(await screen.findByRole('button', { name: 'Add passkey' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText('Current password'), 'my-password');
    await user.click(within(dialog).getByRole('button', { name: 'Create passkey' }));
    expect(await within(dialog).findByText('This authenticator already has a passkey for this server.')).toBeInTheDocument();

    webauthn.create.mockRejectedValue(new DOMException('no', 'NotAllowedError'));
    await user.click(within(dialog).getByRole('button', { name: 'Create passkey' }));
    expect(await within(dialog).findByText('The passkey request was cancelled or timed out.')).toBeInTheDocument();
  });

  it('deletes a passkey after confirming', async () => {
    const user = userEvent.setup();
    webauthn = installWebAuthn();
    const calls = mockServer();
    renderSection();
    const list = await screen.findByRole('list', { name: 'Passkeys' });
    await user.click(within(list).getByRole('button', { name: 'Delete' }));
    await user.click(await screen.findByRole('button', { name: 'Remove' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/web-auth/passkeys/cred-1')).toBe(true));
  });

  it('renames a passkey', async () => {
    const user = userEvent.setup();
    webauthn = installWebAuthn();
    const calls = mockServer();
    renderSection();
    const list = await screen.findByRole('list', { name: 'Passkeys' });
    await user.click(within(list).getByRole('button', { name: 'Rename' }));
    const dialog = await screen.findByRole('dialog');
    const input = within(dialog).getByLabelText('Name');
    await user.clear(input);
    await user.type(input, 'Laptop');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/web-auth/passkeys/cred-1')?.body).toEqual({ name: 'Laptop' }));
  });

  it('explains why passkeys are unavailable over plain HTTP', async () => {
    webauthn = installWebAuthn({ secure: false });
    mockServer();
    renderSection();
    expect(await screen.findByText('Passkeys need HTTPS (or localhost). A passkey only works on the domain where it was created.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Add passkey' })).toBeDisabled());
  });

  it('asks for a password session when the list is refused (LAN bypass, API key)', async () => {
    webauthn = installWebAuthn();
    mockServer({ list: () => envelope(null, 403, 'session_required') });
    renderSection();
    expect(await screen.findByText('Sign in with the password in this browser to manage passkeys.')).toBeInTheDocument();
  });

  it('warns that removing the password removes the passkeys', async () => {
    const user = userEvent.setup();
    webauthn = installWebAuthn();
    mockServer({ passkeyCount: 2 });
    renderSection();
    await user.type(await screen.findByLabelText('Current password'), 'my-password');
    await user.click(screen.getByRole('button', { name: 'Remove password' }));
    expect(await screen.findByText(/Your 2 passkeys are removed too\./)).toBeInTheDocument();
  });

  it('suggests a device name', () => {
    expect(suggestPasskeyName('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36')).toBe('Passkey · Chrome / Windows');
    expect(suggestPasskeyName('')).toBe('Passkey');
  });
});
