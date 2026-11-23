import { create } from 'zustand';
import type { ViewerItem } from './public';

/** What the media viewer overlay shows (the overlay itself is lazy, see PlayerSlot). */
interface ViewerState {
  items: ViewerItem[];
  index: number;
  open: boolean;
  show: (items: ViewerItem[], index: number) => void;
  setIndex: (index: number) => void;
  close: () => void;
}

export const useViewer = create<ViewerState>()((set) => ({
  items: [],
  index: 0,
  open: false,
  show: (items, index) => set({ items, index: Math.min(Math.max(0, index), items.length - 1), open: items.length > 0 }),
  setIndex: (index) => set((s) => ({ index: Math.min(Math.max(0, index), s.items.length - 1) })),
  close: () => set({ open: false })
}));
