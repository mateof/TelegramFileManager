import type { ApiChannelDto } from '@tfm/api-client';
import { Avatar, Badge, Button, cn, Input, Popover, PopoverContent, PopoverTrigger, SegmentedControl } from '@tfm/ui';
import { Check, ChevronsUpDown, Search, Star } from 'lucide-react';
import { useDeferredValue, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { t } from '@/i18n';
import { filterChannels, initialTab, sortChannels, type ChannelComboTab } from './channelFilter';

interface ChannelComboboxProps {
  channels: ApiChannelDto[];
  value: string;
  onChange(id: string): void;
  placeholder: string;
  loading?: boolean;
  id?: string;
  disabled?: boolean;
}

/**
 * Single channel selector with a search box and Mine / Favourites / All tabs.
 * Opens on "Mine" when the account owns channels; the search covers the
 * current tab and offers to jump to "All" when it only matches elsewhere.
 */
export function ChannelCombobox({ channels, value, onChange, placeholder, loading, id, disabled }: ChannelComboboxProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const query = useDeferredValue(search);
  const sorted = useMemo(() => sortChannels(channels), [channels]);
  // Until the user picks a tab, follow the channel list (it may load after the first render)
  const [chosenTab, setChosenTab] = useState<ChannelComboTab | null>(null);
  const tab = chosenTab ?? initialTab(channels);
  const [active, setActive] = useState(0);
  const chooseTab = (v: ChannelComboTab) => {
    setChosenTab(v);
    setActive(0);
  };
  const listId = useId();
  const listRef = useRef<HTMLUListElement>(null);

  const filtered = useMemo(() => filterChannels(sorted, tab, query), [sorted, tab, query]);
  const elsewhere = useMemo(
    () => (tab !== 'all' && query.trim() ? filterChannels(sorted, 'all', query).length - filtered.length : 0),
    [sorted, tab, query, filtered.length]
  );
  const selected = channels.find((c) => String(c.id) === value);

  useEffect(() => {
    listRef.current?.querySelector<HTMLElement>(`[data-index="${active}"]`)?.scrollIntoView({ block: 'nearest' });
  }, [active]);

  const pick = (c: ApiChannelDto) => {
    onChange(String(c.id));
    setOpen(false);
    setSearch('');
  };

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActive((i) => Math.min(i + 1, filtered.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActive((i) => Math.max(i - 1, 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const c = filtered[active];
      if (c) pick(c);
    }
  };

  const tabs: { value: ChannelComboTab; label: string }[] = [
    { value: 'mine', label: t('Mine') },
    { value: 'favourites', label: t('Favourites') },
    { value: 'all', label: t('All') }
  ];

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          variant="secondary"
          role="combobox"
          aria-expanded={open}
          aria-controls={listId}
          disabled={disabled}
          iconRight={<ChevronsUpDown />}
          className="w-full justify-between font-normal"
        >
          {selected ? (
            <span className="flex min-w-0 items-center gap-2">
              <Avatar src={selected.imageUrl} name={selected.name} size="xs" square className="rounded-md" />
              <span className="truncate">{selected.name}</span>
            </span>
          ) : (
            <span className="truncate text-fg-muted">{loading ? t('Loading...') : placeholder}</span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="flex w-[min(28rem,calc(100vw-2rem))] flex-col gap-2 p-2">
        <Input
          autoFocus
          inputSize="sm"
          leading={<Search />}
          placeholder={t('Search channels...')}
          aria-label={t('Search channels...')}
          aria-controls={listId}
          aria-activedescendant={filtered[active] ? `${listId}-${filtered[active].id}` : undefined}
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setActive(0);
          }}
          onKeyDown={onKeyDown}
        />
        <SegmentedControl
          size="sm"
          fullWidth
          aria-label={t('Channel filter')}
          value={tab}
          onValueChange={chooseTab}
          options={tabs}
        />
        <ul id={listId} ref={listRef} role="listbox" aria-label={t('Channels')} className="max-h-72 overflow-y-auto">
          {filtered.length === 0 && (
            <li className="px-2 py-4 text-center text-xs text-fg-muted">
              {loading ? t('Loading...') : t('No results')}
            </li>
          )}
          {filtered.map((c, i) => {
            const isSelected = String(c.id) === value;
            return (
              <li key={c.id} id={`${listId}-${c.id}`} role="option" aria-selected={isSelected} data-index={i}>
                <button
                  type="button"
                  tabIndex={-1}
                  onClick={() => pick(c)}
                  onMouseMove={() => setActive(i)}
                  className={cn(
                    'flex w-full items-center gap-2.5 rounded-lg px-2 py-1.5 text-left text-sm',
                    i === active && 'bg-surface-2',
                    isSelected && 'text-accent-300'
                  )}
                >
                  <Avatar src={c.imageUrl} name={c.name} size="xs" square className="rounded-md" />
                  <span className="min-w-0 flex-1 truncate">{c.name}</span>
                  {c.isOwner && tab !== 'mine' && <Badge tone="accent">{t('Mine')}</Badge>}
                  {c.isFavorite && <Star className="size-3.5 shrink-0 fill-warning text-warning" aria-label={t('Favourites')} />}
                  {isSelected && <Check className="size-4 shrink-0" />}
                </button>
              </li>
            );
          })}
        </ul>
        {elsewhere > 0 && (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => chooseTab('all')}
          >
            {t('{count} more in all channels', { count: elsewhere })}
          </Button>
        )}
      </PopoverContent>
    </Popover>
  );
}
