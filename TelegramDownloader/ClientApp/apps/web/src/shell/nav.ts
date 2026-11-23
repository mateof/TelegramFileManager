import {
  Activity,
  ArrowDownUp,
  Clapperboard,
  FolderTree,
  HardDrive,
  LayoutGrid,
  ListMusic,
  ScrollText,
  Search,
  Settings,
  Share2,
  Wrench,
  type LucideIcon
} from 'lucide-react';
import { t } from '@/i18n';

export interface NavItem {
  id: string;
  /** Path relative to /app. */
  to: string;
  label: string;
  icon: LucideIcon;
  /** Shorter label for the mobile bottom navigation. */
  shortLabel?: string;
  /** Extra words for the command palette. */
  keywords?: string[];
}

export interface NavSection {
  id: string;
  label: string;
  items: NavItem[];
}

/**
 * Main navigation (sidebar, mobile drawer, command palette). Built as a
 * function so the labels are translated at render time.
 */
export function navSections(): NavSection[] {
  return [
    {
      id: 'browse',
      label: t('Browse'),
      items: [
        { id: 'local', to: '/local', label: t('Local files'), icon: HardDrive, keywords: ['disk', 'server'] },
        { id: 'shared', to: '/shared', label: t('Shared collections'), icon: Share2 },
        { id: 'search', to: '/search', label: t('Search'), icon: Search }
      ]
    },
    {
      id: 'media',
      label: t('Media'),
      items: [
        { id: 'playlists', to: '/playlists', label: t('Playlists'), icon: ListMusic, keywords: ['music', 'audio'] },
        { id: 'library', to: '/library', label: t('Library'), icon: Clapperboard, keywords: ['movies', 'series', 'tv'] }
      ]
    },
    {
      id: 'activity',
      label: t('Activity'),
      items: [
        { id: 'transfers', to: '/transfers', label: t('Transfers'), icon: ArrowDownUp, keywords: ['downloads', 'uploads'] },
        { id: 'logs', to: '/logs', label: t('Logs'), icon: ScrollText }
      ]
    },
    {
      id: 'admin',
      label: t('Administration'),
      items: [
        { id: 'settings', to: '/settings', label: t('Settings'), icon: Settings, keywords: ['config', 'preferences'] },
        { id: 'system', to: '/system', label: t('System'), icon: Activity, keywords: ['metrics', 'cpu', 'memory'] },
        { id: 'maintenance', to: '/maintenance', label: t('Maintenance'), icon: Wrench, keywords: ['database', 'import', 'export'] },
        { id: 'apps', to: '/apps', label: t('Apps'), icon: LayoutGrid, keywords: ['android', 'tv', 'player', 'pwa'] }
      ]
    }
  ];
}

/** Bottom navigation on phones (the rest is in the drawer). */
export function mobileNavItems(): NavItem[] {
  return [
    { id: 'channels', to: '/channels', label: t('Channels'), icon: FolderTree },
    { id: 'local', to: '/local', label: t('Local'), icon: HardDrive },
    { id: 'transfers', to: '/transfers', label: t('Transfers'), icon: ArrowDownUp },
    { id: 'playlists', to: '/playlists', label: t('Playlists'), shortLabel: t('Lists'), icon: ListMusic }
  ];
}
