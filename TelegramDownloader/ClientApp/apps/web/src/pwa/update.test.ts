import { toast } from '@tfm/ui';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FOCUS_CHECK_GAP_MS, showUpdateToast, UPDATE_INTERVAL_MS, watchForUpdates, type UpdatableRegistration } from './update';

vi.mock('@tfm/ui', () => ({ toast: vi.fn() }));

describe('update checks', () => {
  let now = 0;
  let registration: UpdatableRegistration & { update: ReturnType<typeof vi.fn> };
  let stop: () => void;

  beforeEach(() => {
    vi.useFakeTimers();
    now = 1_000_000;
    registration = { installing: null, update: vi.fn(() => Promise.resolve()) };
    stop = watchForUpdates(registration, window, () => now);
  });

  afterEach(() => {
    stop();
    vi.useRealTimers();
  });

  it('checks every hour', () => {
    expect(registration.update).not.toHaveBeenCalled();
    now += UPDATE_INTERVAL_MS;
    vi.advanceTimersByTime(UPDATE_INTERVAL_MS);
    expect(registration.update).toHaveBeenCalledTimes(1);
  });

  it('checks on focus, at most once per gap', () => {
    window.dispatchEvent(new Event('focus'));
    expect(registration.update).not.toHaveBeenCalled(); // just registered

    now += FOCUS_CHECK_GAP_MS;
    window.dispatchEvent(new Event('focus'));
    document.dispatchEvent(new Event('visibilitychange'));
    expect(registration.update).toHaveBeenCalledTimes(1);
  });

  it('does not check while a new version is installing or offline', () => {
    now += FOCUS_CHECK_GAP_MS;
    (registration as { installing: unknown }).installing = {};
    window.dispatchEvent(new Event('focus'));
    expect(registration.update).not.toHaveBeenCalled();

    (registration as { installing: unknown }).installing = null;
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    window.dispatchEvent(new Event('focus'));
    expect(registration.update).not.toHaveBeenCalled();
    online.mockRestore();
  });

  it('stops checking when disposed', () => {
    stop();
    now += UPDATE_INTERVAL_MS;
    vi.advanceTimersByTime(UPDATE_INTERVAL_MS);
    window.dispatchEvent(new Event('focus'));
    expect(registration.update).not.toHaveBeenCalled();
  });
});

describe('update toast', () => {
  it('stays until the user reloads, and Reload activates the new version', () => {
    const reload = vi.fn();
    showUpdateToast(reload);
    expect(toast).toHaveBeenCalledWith(
      'New version available',
      expect.objectContaining({ duration: Infinity, action: expect.objectContaining({ label: 'Reload' }) })
    );
    const options = vi.mocked(toast).mock.calls[0][1] as unknown as { action: { onClick: () => void } };
    options.action.onClick();
    expect(reload).toHaveBeenCalledOnce();
  });
});
