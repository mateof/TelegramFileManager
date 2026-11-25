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
  /** File manager: open folders with a single click (mouse). */
  singleClickFolders: boolean;
  /** Mobile drawer: ids of the navigation sections folded away. */
  drawerCollapsedSections: string[];
  /** Mobile drawer: the channel list takes the whole drawer (navigation hidden). */
  drawerChannelsExpanded: boolean;
  toggleSidebar: () => void;
  setMobileNavOpen: (open: boolean) => void;
  setCommandOpen: (open: boolean) => void;
  setChannelTab: (tab: ChannelTab) => void;
  setFavouritesOnly: (on: boolean) => void;
  setSingleClickFolders: (on: boolean) => void;
  toggleDrawerSection: (id: string) => void;
  setDrawerChannelsExpanded: (on: boolean) => void;
}

export const useUi = create<UiState>()(
  persist(
    (set) => ({
      sidebarCollapsed: false,
      mobileNavOpen: false,
      commandOpen: false,
      channelTab: 'all',
      favouritesOnly: false,
      singleClickFolders: true,
      drawerCollapsedSections: [],
      drawerChannelsExpanded: false,
      toggleSidebar: () => set((s) => ({ sidebarCollapsed: !s.sidebarCollapsed })),
      setMobileNavOpen: (mobileNavOpen) => set({ mobileNavOpen }),
      setCommandOpen: (commandOpen) => set({ commandOpen }),
      setChannelTab: (channelTab) => set({ channelTab }),
      setFavouritesOnly: (favouritesOnly) => set({ favouritesOnly }),
      setSingleClickFolders: (singleClickFolders) => set({ singleClickFolders }),
      toggleDrawerSection: (id) =>
        set((s) => ({
          drawerCollapsedSections: s.drawerCollapsedSections.includes(id)
            ? s.drawerCollapsedSections.filter((x) => x !== id)
            : [...s.drawerCollapsedSections, id]
        })),
      setDrawerChannelsExpanded: (drawerChannelsExpanded) => set({ drawerChannelsExpanded })
    }),
    {
      name: 'tfm.web.ui',
      partialize: ({ sidebarCollapsed, channelTab, favouritesOnly, singleClickFolders, drawerCollapsedSections, drawerChannelsExpanded }) => ({
        sidebarCollapsed,
        channelTab,
        favouritesOnly,
        singleClickFolders,
        drawerCollapsedSections,
        drawerChannelsExpanded
      })
    }
  )
);
