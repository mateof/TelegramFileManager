import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { installModeOf, isIos, promptInstall, startInstallCapture, useInstallMode, useInstallStore, type BeforeInstallPromptEvent } from './install';

function fakePrompt(outcome: 'accepted' | 'dismissed') {
  const event = new Event('beforeinstallprompt', { cancelable: true }) as BeforeInstallPromptEvent;
  const prompt = vi.fn(() => Promise.resolve());
  Object.assign(event, { prompt, userChoice: Promise.resolve({ outcome, platform: 'web' }) });
  return { event, prompt };
}

afterEach(() => useInstallStore.setState({ deferred: null, installed: false }));

describe('install mode', () => {
  const secure = { secure: true, ios: false };
  const deferred = {} as BeforeInstallPromptEvent;

  it('offers nothing once installed', () => {
    expect(installModeOf({ deferred, installed: true }, secure)).toBe('installed');
  });

  it('offers the browser prompt when available', () => {
    expect(installModeOf({ deferred, installed: false }, secure)).toBe('prompt');
  });

  it('explains HTTPS on insecure origins, then iOS, then the browser menu', () => {
    expect(installModeOf({ deferred: null, installed: false }, { secure: false, ios: true })).toBe('insecure');
    expect(installModeOf({ deferred: null, installed: false }, { secure: true, ios: true })).toBe('ios');
    expect(installModeOf({ deferred: null, installed: false }, secure)).toBe('manual');
  });

  it('detects iPhone, iPad and iPadOS desktop mode', () => {
    expect(isIos('Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15', 5)).toBe(true);
    expect(isIos('Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)', 5)).toBe(true);
    expect(isIos('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Safari/605.1.15', 5)).toBe(true);
    expect(isIos('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Safari/605.1.15', 0)).toBe(false);
    expect(isIos('Mozilla/5.0 (Linux; Android 14) Chrome/130', 5)).toBe(false);
  });
});

describe('install prompt capture', () => {
  it('keeps the deferred prompt, uses it once and marks the app installed', async () => {
    startInstallCapture(window);
    const { result } = renderHook(() => useInstallMode());
    expect(result.current).toBe('insecure'); // jsdom is not a secure context

    const { event, prompt } = fakePrompt('accepted');
    act(() => void window.dispatchEvent(event));
    expect(event.defaultPrevented).toBe(true); // no browser mini-infobar
    expect(result.current).toBe('prompt');

    let outcome = '';
    await act(async () => void (outcome = await promptInstall()));
    expect(prompt).toHaveBeenCalledOnce();
    expect(outcome).toBe('accepted');
    expect(result.current).toBe('installed');
    expect(await promptInstall()).toBe('unavailable');
  });

  it('a dismissed prompt cannot be reused', async () => {
    startInstallCapture(window);
    const { event } = fakePrompt('dismissed');
    window.dispatchEvent(event);
    expect(await promptInstall()).toBe('dismissed');
    expect(useInstallStore.getState()).toMatchObject({ deferred: null, installed: false });
  });

  it('hides the offer after the appinstalled event', () => {
    startInstallCapture(window);
    window.dispatchEvent(fakePrompt('accepted').event);
    window.dispatchEvent(new Event('appinstalled'));
    expect(useInstallStore.getState()).toMatchObject({ deferred: null, installed: true });
  });
});
