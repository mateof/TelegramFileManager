import { create } from 'zustand';
import type { PlaylistCandidate } from './public';

/** State of the "add to playlist" picker (the dialog itself is lazy). */
export const usePlaylistPicker = create<{ items: PlaylistCandidate[]; open: boolean }>()(() => ({ items: [], open: false }));
