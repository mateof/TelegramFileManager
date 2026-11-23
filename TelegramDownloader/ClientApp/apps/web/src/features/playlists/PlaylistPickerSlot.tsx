import { lazy, Suspense } from 'react';
import { usePlaylistPicker } from './pickerStore';

const AddToPlaylistDialog = lazy(() => import('./AddToPlaylistDialog'));

/** Mounts the add-to-playlist dialog (lazy) while it is open. Rendered by the player slot. */
export function PlaylistPickerSlot() {
  const open = usePlaylistPicker((s) => s.open);
  if (!open) return null;
  return (
    <Suspense fallback={null}>
      <AddToPlaylistDialog />
    </Suspense>
  );
}
