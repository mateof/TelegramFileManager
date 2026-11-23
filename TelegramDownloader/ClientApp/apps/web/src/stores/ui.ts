import { create } from 'zustand';
import { persist } from 'zustand/middleware';

export type ChannelTab = 'mine' | 'favourites' | 'shared' | 'all' | 'folders';

interface UiState {
  /** Desktop sidebar collapsed to an icon rail. */
  sidebarCollapsed: boolean;
  /** Mobile drawer open. */
  mobileNavOpen: boolean;
  commandOpen: boolean;
  channelTab: ChannelTab;
  /** Sidebar shows favourites only (star toggle). */
  favouritesOnly: boolean;
  toggleSidebar: () => void;
  setMobileNavOpen: (open: boolean) => void;
  setCommandOpen: (open: boolean) => void;
  setChannelTab: (tab: ChannelTab) => void;
  setFavouritesOnly: (on: boolean) => void;
}

export const useUi = create<UiState>()(
  persist(
    (set) => ({
      sidebarCollapsed: false,
      mobileNavOpen: false,
      commandOpen: false,
      channelTab: 'all',
      favouritesOnly: false,
      toggleSidebar: () => set((s) => ({ sidebarCollapsed: !s.sidebarCollapsed })),
      setMobileNavOpen: (mobileNavOpen) => set({ mobileNavOpen }),
      setCommandOpen: (commandOpen) => set({ commandOpen }),
      setChannelTab: (channelTab) => set({ channelTab }),
      setFavouritesOnly: (favouritesOnly) => set({ favouritesOnly })
    }),
    {
      name: 'tfm.web.ui',
      partialize: ({ sidebarCollapsed, channelTab, favouritesOnly }) => ({ sidebarCollapsed, channelTab, favouritesOnly })
    }
  )
);
