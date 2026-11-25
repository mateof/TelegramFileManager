import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { queryClient } from '@/lib/queryClient';
import { App } from './App';

function envelope(data: unknown, status = 200) {
  return new Response(JSON.stringify({ success: status < 400, data, error: status < 400 ? null : { code: 'x', message: 'x' } }), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

function mockServer(routes: Record<string, () => Response>) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const path = url.replace(/^https?:\/\/[^/]+/, '').split('?')[0];
    const handler = routes[path];
    return handler ? handler() : envelope(null, 404);
  });
}

describe('App auth gate', () => {
  beforeEach(() => {
    window.history.pushState({}, '', '/app/');
  });
  afterEach(() => {
    queryClient.clear();
  });

  it('shows the login screen when there is no web session', async () => {
    mockServer({
      '/api/v1/web-auth/status': () => envelope({ passwordSet: true, authenticated: false, lanBypassEnabled: false, lanBypassActive: false })
    });
    render(<App />);
    expect(await screen.findByText('Welcome back')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeDisabled();
  });

  it('asks to create a password on a fresh install', async () => {
    mockServer({
      '/api/v1/web-auth/status': () => envelope({ passwordSet: false, authenticated: false, lanBypassEnabled: false, lanBypassActive: false })
    });
    render(<App />);
    expect(await screen.findByText('Protect this server')).toBeInTheDocument();
  });

  it('shows the Telegram login when the web session is valid but Telegram is not', async () => {
    mockServer({
      '/api/v1/web-auth/status': () => envelope({ passwordSet: true, authenticated: true, lanBypassEnabled: false, lanBypassActive: false }),
      '/api/v1/auth/status': () => envelope({ step: 'phone', isAuthenticated: false, isConfigured: true, user: null }),
      '/api/v1/auth/qr': () => envelope({ sessionId: 's1', loginUrl: null, qrImageBase64: null, status: 'waiting', error: null })
    });
    render(<App />);
    expect(await screen.findByText('Connect your Telegram account')).toBeInTheDocument();
  });
});
