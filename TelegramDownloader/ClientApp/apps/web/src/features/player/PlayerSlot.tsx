import { lazy, Suspense, useEffect } from 'react';
import { PlaylistPickerSlot } from '@/features/playlists/PlaylistPickerSlot';
import { selectHasQueue, usePlayer } from './store';
import { useViewer } from './viewerStore';

const PlayerBar = lazy(() => import('./ui/PlayerBar'));
const MediaViewer = lazy(() => import('./viewer/MediaViewer'));

/**
 * Global media slot, rendered by the layout under the content (above the
 * mobile bottom nav) on every page, so playback survives navigation.
 *
 * Only the stores live in the initial bundle: the player bar (with the audio
 * engine), the viewers and the playlist picker are separate chunks loaded when
 * they are needed. Returns nothing while the queue is empty.
 */
export function PlayerSlot() {
  const hasQueue = usePlayer(selectHasQueue);
  const viewerOpen = useViewer((s) => s.open);

  // Warm the engine chunk once the app is idle, so the first "play" starts
  // within the click's user activation (autoplay policies)
  useEffect(() => {
    const load = () => void import('./engine').catch(() => undefined);
    if ('requestIdleCallback' in window) {
      const id = window.requestIdleCallback(load, { timeout: 5000 });
      return () => window.cancelIdleCallback(id);
    }
    const id = setTimeout(load, 3000);
    return () => clearTimeout(id);
  }, []);

  return (
    <>
      {hasQueue && (
        <Suspense fallback={null}>
          <PlayerBar />
        </Suspense>
      )}
      {viewerOpen && (
        <Suspense fallback={null}>
          <MediaViewer />
        </Suspense>
      )}
      <PlaylistPickerSlot />
    </>
  );
}
