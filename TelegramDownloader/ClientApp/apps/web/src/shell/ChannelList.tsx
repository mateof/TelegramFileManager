import type { ApiChannelDto, ApiChannelFolderDto } from '@tfm/api-client';
import { Avatar, Button, cn, EmptyState, Input, Skeleton, Tooltip } from '@tfm/ui';
import { useVirtualizer } from '@tanstack/react-virtual';
import { ChevronRight, FolderTree, Search, Star, X } from 'lucide-react';
import { memo, useDeferredValue, useMemo, useRef, useState } from 'react';
import { NavLink } from 'react-router';
import { t } from '@/i18n';
import { errorMessage } from '@/lib/api';
import { useUi, type ChannelTab } from '@/stores/ui';
import { useChannelFolders, useChannels, useToggleFavourite } from './useChannels';

const ROW = 40;

function tabs(): { id: ChannelTab; label: string }[] {
  return [
    { id: 'all', label: t('All') },
    { id: 'mine', label: t('Mine') },
    { id: 'favourites', label: t('Favourites') },
    { id: 'shared', label: t('Shared') },
    { id: 'folders', label: t('Folders') }
  ];
}

function matches(c: ApiChannelDto, q: string) {
  return !q || c.name.toLowerCase().includes(q);
}

const ChannelRow = memo(function ChannelRow({
  channel,
  collapsed,
  onNavigate
}: {
  channel: ApiChannelDto;
  collapsed: boolean;
  onNavigate?: () => void;
}) {
  const toggle = useToggleFavourite();
  const link = (
    <NavLink
      to={`/channels/${channel.id}`}
      onClick={onNavigate}
      className={({ isActive }) =>
        cn(
          'group/row relative flex h-9 items-center gap-2.5 rounded-lg px-2 text-sm text-fg-muted outline-none transition-colors',
          'hover:bg-surface-2 hover:text-fg focus-visible:ring-2 focus-visible:ring-ring',
          isActive && 'bg-primary-soft font-medium text-fg before:absolute before:inset-y-2 before:left-0 before:w-0.5 before:rounded-full before:bg-primary',
          collapsed && 'justify-center px-0'
        )
      }
    >
      <Avatar src={channel.imageUrl} name={channel.name} size="xs" square className="rounded-md" />
      {!collapsed && (
        <>
          <span className="min-w-0 flex-1 truncate">{channel.name}</span>
          <button
            type="button"
            aria-label={channel.isFavorite ? t('Remove from favourites') : t('Add to favourites')}
            aria-pressed={channel.isFavorite}
            onClick={(e) => {
              e.preventDefault();
              e.stopPropagation();
              toggle.mutate({ id: channel.id, favourite: !channel.isFavorite });
            }}
            className={cn(
              'flex size-6 shrink-0 items-center justify-center rounded-md text-fg-subtle transition-opacity hover:bg-surface-3 hover:text-warning focus-visible:opacity-100',
              channel.isFavorite ? 'text-warning opacity-100' : 'opacity-0 group-hover/row:opacity-100'
            )}
          >
            <Star className={cn('size-3.5', channel.isFavorite && 'fill-current')} />
          </button>
        </>
      )}
    </NavLink>
  );
  return collapsed ? (
    <Tooltip content={channel.name} side="right">
      {link}
    </Tooltip>
  ) : (
    link
  );
});

function VirtualChannels({ channels, collapsed, onNavigate }: { channels: ApiChannelDto[]; collapsed: boolean; onNavigate?: () => void }) {
  const parentRef = useRef<HTMLDivElement>(null);
  // eslint-disable-next-line react-hooks/incompatible-library -- TanStack Virtual is not compiler-memoizable; fine here
  const virtualizer = useVirtualizer({
    count: channels.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => ROW,
    overscan: 10
  });
  return (
    <div ref={parentRef} className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-2 pb-2">
      <div className="relative w-full" style={{ height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((row) => (
          <div key={row.key} className="absolute inset-x-0" style={{ top: row.start, height: ROW, paddingBottom: 4 }}>
            <ChannelRow channel={channels[row.index]} collapsed={collapsed} onNavigate={onNavigate} />
          </div>
        ))}
      </div>
    </div>
  );
}

function FolderGroup({ folder, query, onNavigate }: { folder: ApiChannelFolderDto; query: string; onNavigate?: () => void }) {
  const [open, setOpen] = useState(false);
  const list = folder.channels.filter((c) => matches(c, query));
  if (query && list.length === 0) return null;
  const expanded = open || !!query;
  return (
    <div>
      <button
        type="button"
        aria-expanded={expanded}
        onClick={() => setOpen((o) => !o)}
        className="flex h-9 w-full items-center gap-2 rounded-lg px-2 text-sm text-fg-muted hover:bg-surface-2 hover:text-fg"
      >
        <ChevronRight className={cn('size-3.5 shrink-0 transition-transform', expanded && 'rotate-90')} />
        <span aria-hidden className="w-5 text-center">{folder.iconEmoji || '📁'}</span>
        <span className="min-w-0 flex-1 truncate text-left">{folder.title}</span>
        <span className="text-xs text-fg-subtle">{list.length}</span>
      </button>
      {expanded && (
        <div className="ml-4 flex flex-col gap-1 border-l border-border pl-2">
          {list.map((c) => (
            <ChannelRow key={c.id} channel={c} collapsed={false} onNavigate={onNavigate} />
          ))}
        </div>
      )}
    </div>
  );
}

function FoldersView({ query, onNavigate }: { query: string; onNavigate?: () => void }) {
  const folders = useChannelFolders(true);
  if (folders.isPending) return <ListSkeleton />;
  if (folders.isError) return <p className="px-4 py-3 text-xs text-danger">{errorMessage(folders.error)}</p>;
  const ungrouped = folders.data.ungrouped.filter((c) => matches(c, query));
  return (
    <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-2 pb-2">
      {folders.data.folders.map((f) => (
        <FolderGroup key={f.id} folder={f} query={query} onNavigate={onNavigate} />
      ))}
      {ungrouped.length > 0 && (
        <FolderGroup
          folder={{ id: -1, title: t('Not in a folder'), iconEmoji: '🗂️', channels: ungrouped, channelCount: ungrouped.length }}
          query={query}
          onNavigate={onNavigate}
        />
      )}
    </div>
  );
}

function ListSkeleton() {
  return (
    <div className="flex flex-col gap-2 px-4 py-2">
      {Array.from({ length: 8 }, (_, i) => (
        <div key={i} className="flex items-center gap-2.5">
          <Skeleton className="size-6 rounded-md" />
          <Skeleton className="h-3" style={{ width: `${50 + ((i * 37) % 40)}%` }} />
        </div>
      ))}
    </div>
  );
}

export interface ChannelListProps {
  collapsed?: boolean;
  /** Called after following a link (closes the mobile drawer). */
  onNavigate?: () => void;
}

/** Sidebar channel browser: tabs Mine / Favourites / Shared / All / Folders, search, favourite toggle. */
export function ChannelList({ collapsed = false, onNavigate }: ChannelListProps) {
  const tab = useUi((s) => s.channelTab);
  const setTab = useUi((s) => s.setChannelTab);
  const favouritesOnly = useUi((s) => s.favouritesOnly);
  const setFavouritesOnly = useUi((s) => s.setFavouritesOnly);
  const [search, setSearch] = useState('');
  const query = useDeferredValue(search.trim().toLowerCase());
  const channels = useChannels();

  const filtered = useMemo(() => {
    const list = channels.data ?? [];
    return list.filter((c) => {
      if (!matches(c, query)) return false;
      if (favouritesOnly && !c.isFavorite) return false;
      switch (tab) {
        case 'mine':
          return c.isOwner;
        case 'favourites':
          return c.isFavorite;
        case 'shared':
          return !c.isOwner;
        default:
          return true;
      }
    });
  }, [channels.data, query, tab, favouritesOnly]);

  if (collapsed) {
    const favourites = (channels.data ?? []).filter((c) => c.isFavorite);
    return <VirtualChannels channels={favourites.length ? favourites : (channels.data ?? [])} collapsed onNavigate={onNavigate} />;
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex items-center justify-between px-4 pb-1.5 pt-3">
        <span className="text-[11px] font-semibold uppercase tracking-wider text-fg-subtle">{t('Channels')}</span>
        {channels.data && <span className="text-[11px] text-fg-subtle">{channels.data.length}</span>}
      </div>
      <div className="flex items-center gap-1.5 px-3 pb-2">
        <Input
          inputSize="sm"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t('Filter channels')}
          aria-label={t('Filter channels')}
          leading={<Search />}
          trailing={
            search ? (
              <button type="button" aria-label={t('Clear')} onClick={() => setSearch('')} className="flex text-fg-subtle hover:text-fg">
                <X className="size-3.5" />
              </button>
            ) : undefined
          }
          className="flex-1"
        />
        <Tooltip content={favouritesOnly ? t('Show all channels') : t('Only favourites')}>
          <button
            type="button"
            aria-label={t('Only favourites')}
            aria-pressed={favouritesOnly}
            onClick={() => setFavouritesOnly(!favouritesOnly)}
            className={cn(
              'flex size-8 shrink-0 items-center justify-center rounded-lg border border-border text-fg-subtle transition-colors hover:text-warning',
              favouritesOnly && 'border-warning/40 bg-warning-soft text-warning'
            )}
          >
            <Star className={cn('size-4', favouritesOnly && 'fill-current')} />
          </button>
        </Tooltip>
      </div>
      <div role="tablist" aria-label={t('Channel filter')} className="flex gap-0.5 overflow-x-auto px-3 pb-2 [scrollbar-width:none]">
        {tabs().map((tb) => (
          <button
            key={tb.id}
            role="tab"
            type="button"
            aria-selected={tab === tb.id}
            onClick={() => setTab(tb.id)}
            className={cn(
              'shrink-0 rounded-md px-2 py-1 text-xs font-medium text-fg-muted transition-colors hover:bg-surface-2 hover:text-fg',
              tab === tb.id && 'bg-surface-2 text-fg shadow-soft'
            )}
          >
            {tb.label}
          </button>
        ))}
      </div>
      {tab === 'folders' ? (
        <FoldersView query={query} onNavigate={onNavigate} />
      ) : channels.isPending ? (
        <ListSkeleton />
      ) : channels.isError ? (
        <div className="px-4 py-3 text-xs text-danger">
          {errorMessage(channels.error)}
          <Button variant="link" size="xs" className="ml-2" onClick={() => void channels.refetch()}>
            {t('Retry')}
          </Button>
        </div>
      ) : filtered.length === 0 ? (
        <EmptyState icon={<FolderTree />} title={t('No channels')} className="py-8" />
      ) : (
        <VirtualChannels channels={filtered} collapsed={false} onNavigate={onNavigate} />
      )}
    </div>
  );
}
