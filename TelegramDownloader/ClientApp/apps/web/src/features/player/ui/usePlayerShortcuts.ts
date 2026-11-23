import { useEffect } from 'react';
import { getEngine } from '../engine';
import { usePlayer } from '../store';
import { useViewer } from '../viewerStore';

/** Elements that use the keys themselves (typing, sliders, lists, menus...). */
const INTERACTIVE =
  'input, textarea, select, [contenteditable=""], [contenteditable="true"], [role="slider"], [role="listbox"], [role="grid"], [role="tree"], [role="menu"], [role="tablist"], [role="radiogroup"], [data-no-player-keys]';

/** Whether a key event belongs to the player (and not to a field or another dialog). */
export function isPlayerKeyTarget(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return true;
  if (target.closest(INTERACTIVE)) return false;
  // Inside another dialog (a modal has the focus): leave its keys alone, except in the expanded player
  const dialog = target.closest('[role="dialog"], [role="alertdialog"]');
  return !dialog || !!dialog.closest('[data-player-keys]');
}

/**
 * Global player shortcuts:
 * Space play/pause, ←/→ seek 10 s, Shift+←/→ previous/next track,
 * Shift+↑/↓ volume, M mute.
 */
export function usePlayerShortcuts() {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
      if (useViewer.getState().open) return; // the viewer has its own keys
      if (!isPlayerKeyTarget(e.target)) return;
      // Space on a button activates the button
      if (e.key === ' ' && e.target instanceof HTMLElement && e.target.closest('button, a, [role="button"]')) return;
      const engine = getEngine();
      const { volume } = usePlayer.getState();
      let handled = true;
      switch (e.key) {
        case ' ':
        case 'MediaPlayPause':
          engine.toggle();
          break;
        case 'ArrowLeft':
          if (e.shiftKey) void engine.previous();
          else engine.seekBy(-10);
          break;
        case 'ArrowRight':
          if (e.shiftKey) void engine.next();
          else engine.seekBy(10);
          break;
        case 'ArrowUp':
          if (e.shiftKey) engine.setVolume(volume + 0.05);
          else handled = false;
          break;
        case 'ArrowDown':
          if (e.shiftKey) engine.setVolume(volume - 0.05);
          else handled = false;
          break;
        case 'm':
        case 'M':
          engine.toggleMute();
          break;
        default:
          handled = false;
      }
      if (handled) e.preventDefault();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);
}
