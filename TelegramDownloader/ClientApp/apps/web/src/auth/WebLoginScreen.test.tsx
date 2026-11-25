import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fakeCredential, installWebAuthn, pendingUntilAborted } from '@/test/webauthnMock';
import { WebLoginScreen } from './WebAuthScreens';

function envelope(data: unknown, status = 200, code = 'x') {
  return new Response(JSON.stringify({ success: status < 400, data, error: status < 400 ? null : { code, message: code } }), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

interface Call {
  path: string;
  method: string;
  body: Record<string, unknown> | null;
  headers: Record<string, string>;
}

function mockServer(finish: () => Response = () => envelope({ passwordSet: true, authenticated: true })) {
  const calls: Call[] = [];
  let n = 0;
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const path = url.replace(/^https?:\/\/[^/]+/, '').split('?')[0];
    const method = init?.method ?? 'GET';
    calls.push({ path, method, body: init?.body ? (JSON.parse(String(init.body)) as Record<string, unknown>) : null, headers: (init?.headers ?? {}) as Record<string, string> });
    if (path === '/api/v1/web-auth/passkeys/login/begin') {
      n++;
      return envelope({ requestId: `req-${n}`, options: { challenge: 'Y2hhbA', rpId: 'localhost', userVerification: 'preferred' } });
    }
    if (path === '/api/v1/web-auth/passkeys/login/finish') return finish();
    if (path === '/api/v1/web-auth/status') return envelope({ passwordSet: true, authenticated: true, lanBypassEnabled: false, lanBypassActive: false });
    return envelope(null, 404);
  });
  return calls;
}

function renderLogin(passkeys = true) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <WebLoginScreen passkeys={passkeys} />
    </QueryClientProvider>
  );
}

let webauthn: ReturnType<typeof installWebAuthn> | null = null;
afterEach(() => {
  webauthn?.restore();
  webauthn = null;
});

describe('web login with passkeys', () => {
  it('signs in with the passkey button and keeps "remember me"', async () => {
    const user = userEvent.setup();
    webauthn = installWebAuthn();
    webauthn.get.mockResolvedValue(fakeCredential('get'));
    const calls = mockServer();
    renderLogin();

    await user.click(screen.getByRole('checkbox', { name: 'Keep me signed in on this device' }));
    await user.click(screen.getByRole('button', { name: 'Sign in with a passkey' }));

    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/login/finish'))).toBe(true));
    const finish = calls.find((c) => c.path.endsWith('/login/finish'))!;
    expect(finish.method).toBe('POST');
    expect(finish.headers['X-Requested-With']).toBe('tfm');
    expect(finish.body).toMatchObject({ requestId: 'req-1', remember: false });
    expect((finish.body!.credential as Record<string, unknown>).id).toBe('cred-1');
    expect(webauthn.get.mock.calls[0][0].mediation).toBeUndefined();
  });

  it('offers passkeys in the autofill and aborts that request before the dialog', async () => {
    const user = userEvent.setup();
    webauthn = installWebAuthn({ conditional: true });
    let conditionalSignal: AbortSignal | null | undefined;
    webauthn.get.mockImplementation(async (options) => {
      if (options.mediation === 'conditional') {
        conditionalSignal = options.signal;
        return pendingUntilAborted(options.signal);
      }
      return fakeCredential('get');
    });
    const calls = mockServer();
    renderLogin();

    expect(screen.getByLabelText('Password')).toHaveAttribute('autocomplete', 'current-password webauthn');
    await waitFor(() => expect(webauthn!.get).toHaveBeenCalledTimes(1));
    expect(webauthn.get.mock.calls[0][0].mediation).toBe('conditional');

    await user.click(screen.getByRole('button', { name: 'Sign in with a passkey' }));
    expect(conditionalSignal?.aborted).toBe(true);
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/login/finish'))).toBe(true));
    // The dialog used its own challenge
    expect(calls.find((c) => c.path.endsWith('/login/finish'))!.body).toMatchObject({ requestId: 'req-2' });
  });

  it('finishes the sign-in when a passkey is picked from the autofill', async () => {
    webauthn = installWebAuthn({ conditional: true });
    webauthn.get.mockResolvedValue(fakeCredential('get'));
    const calls = mockServer();
    renderLogin();
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/login/finish'))).toBe(true));
    expect(calls.find((c) => c.path.endsWith('/login/finish'))!.body).toMatchObject({ requestId: 'req-1', remember: true });
    expect(webauthn.get.mock.calls[0][0].mediation).toBe('conditional');
  });

  it('shows friendly errors', async () => {
    const user = userEvent.setup();
    webauthn = installWebAuthn();
    webauthn.get.mockRejectedValueOnce(new DOMException('user said no', 'NotAllowedError'));
    mockServer(() => envelope(null, 401, 'passkey_invalid'));
    renderLogin();

    await user.click(screen.getByRole('button', { name: 'Sign in with a passkey' }));
    expect(await screen.findByText('The passkey request was cancelled or timed out.')).toBeInTheDocument();

    webauthn.get.mockResolvedValue(fakeCredential('get'));
    await user.click(screen.getByRole('button', { name: 'Sign in with a passkey' }));
    expect(await screen.findByText(/This passkey was not accepted/)).toBeInTheDocument();
  });

  it('hides passkeys outside a secure context, and when none is registered', () => {
    webauthn = installWebAuthn({ secure: false });
    mockServer();
    renderLogin();
    expect(screen.queryByRole('button', { name: 'Sign in with a passkey' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Password')).toHaveAttribute('autocomplete', 'current-password');
    webauthn.restore();

    webauthn = installWebAuthn();
    renderLogin(false);
    expect(screen.queryAllByRole('button', { name: 'Sign in with a passkey' })).toHaveLength(0);
  });
});
