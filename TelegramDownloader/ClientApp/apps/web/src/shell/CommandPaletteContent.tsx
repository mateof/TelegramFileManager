import { Avatar, CommandPalette, Kbd, useAppearance, type CommandGroup } from '@tfm/ui';
import { FolderTree, LayoutGrid, PanelLeft, Search, SunMoon, Undo2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router';
import { t } from '@/i18n';
import { useUi } from '@/stores/ui';
import { useCommandRegistry } from './commands';
import { navSections } from './nav';
import { useChannels } from './useChannels';

/** Palette content (lazy: cmdk is loaded the first time the palette opens). */
export default function CommandPaletteContent() {
  const open = useUi((s) => s.commandOpen);
  const setOpen = useUi((s) => s.setCommandOpen);
  const toggleSidebar = useUi((s) => s.toggleSidebar);
  const toggleLight = useAppearance((s) => s.toggleLight);
  const navigate = useNavigate();
  const channels = useChannels();
  const extra = useCommandRegistry((s) => s.groups);
  const [search, setSearch] = useState('');

  const groups = useMemo<CommandGroup[]>(() => {
    const pages: CommandGroup = {
      id: 'pages',
      heading: t('Pages'),
      items: [
        { id: 'channels', label: t('Channels'), icon: <FolderTree />, onSelect: () => navigate('/channels') },
        ...navSections().flatMap((s) =>
          s.items.map((item) => {
            const Icon = item.icon;
            return { id: item.id, label: item.label, keywords: item.keywords, icon: <Icon />, onSelect: () => navigate(item.to) };
          })
        )
      ]
    };
    const channelGroup: CommandGroup = {
      id: 'channels',
      heading: t('Channels'),
      items: (channels.data ?? []).map((c) => ({
        id: String(c.id),
        label: c.name,
        keywords: [String(c.id)],
        icon: <Avatar src={c.imageUrl} name={c.name} size="xs" square className="size-5 rounded" />,
        hint: c.isFavorite ? '★' : undefined,
        onSelect: () => navigate(`/channels/${c.id}`)
      }))
    };
    const actions: CommandGroup = {
      id: 'actions',
      heading: t('Actions'),
      items: [
        ...(search.trim()
          ? [
              {
                id: 'search-files',
                label: t('Search files for "{query}"', { query: search.trim() }),
                keywords: [search],
                icon: <Search />,
                onSelect: () => navigate(`/search?q=${encodeURIComponent(search.trim())}`)
              }
            ]
          : []),
        { id: 'theme', label: t('Toggle light / dark theme'), icon: <SunMoon />, onSelect: toggleLight },
        { id: 'sidebar', label: t('Toggle sidebar'), icon: <PanelLeft />, onSelect: toggleSidebar },
        { id: 'apps', label: t('Get the mobile and TV apps'), icon: <LayoutGrid />, onSelect: () => navigate('/apps') },
        { id: 'classic', label: t('Open the classic interface'), icon: <Undo2 />, onSelect: () => window.location.assign('/') }
      ]
    };
    return [actions, pages, ...Object.values(extra), channelGroup];
  }, [channels.data, extra, navigate, search, toggleLight, toggleSidebar]);

  return (
    <CommandPalette
      open={open}
      onOpenChange={setOpen}
      groups={groups}
      search={search}
      onSearchChange={setSearch}
      placeholder={t('Search channels, pages and actions...')}
      emptyText={t('No results')}
      footer={
        <>
          <span className="flex items-center gap-1">
            <Kbd>↑</Kbd>
            <Kbd>↓</Kbd> {t('to move')}
          </span>
          <span className="flex items-center gap-1">
            <Kbd>↵</Kbd> {t('to open')}
          </span>
        </>
      }
    />
  );
}
