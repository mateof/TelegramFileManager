import { Badge, Button, cn, Input, Popover, PopoverContent, PopoverTrigger } from '@tfm/ui';
import { Check, ChevronsUpDown, Search, X } from 'lucide-react';
import { useMemo, useState } from 'react';
import { t } from '@/i18n';
import { channelName, type ChannelOption } from './channelOptions';

function useFiltered(options: ChannelOption[], query: string) {
  return useMemo(() => {
    const q = query.trim().toLowerCase();
    const list = q ? options.filter((c) => c.name.toLowerCase().includes(q) || String(c.id).includes(q)) : options;
    return list.slice(0, 200);
  }, [options, query]);
}

function OptionList({ options, isSelected, onPick, query, setQuery }: { options: ChannelOption[]; isSelected: (id: number) => boolean; onPick: (id: number) => void; query: string; setQuery: (q: string) => void }) {
  const filtered = useFiltered(options, query);
  return (
    <div className="flex flex-col gap-2">
      <Input autoFocus inputSize="sm" leading={<Search />} placeholder={t('Search channels...')} value={query} onChange={(e) => setQuery(e.target.value)} aria-label={t('Search channels...')} />
      <ul role="listbox" aria-multiselectable className="max-h-64 overflow-y-auto">
        {filtered.length === 0 && <li className="px-2 py-3 text-center text-xs text-fg-muted">{t('No results')}</li>}
        {filtered.map((c) => {
          const selected = isSelected(c.id);
          return (
            <li key={c.id} role="option" aria-selected={selected}>
              <button
                type="button"
                onClick={() => onPick(c.id)}
                className={cn('flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm hover:bg-surface-2', selected && 'text-accent-300')}
              >
                <span className={cn('flex size-4 shrink-0 items-center justify-center rounded border border-border-strong', selected && 'border-accent-500 bg-primary text-on-accent')}>
                  {selected && <Check className="size-3" strokeWidth={3} />}
                </span>
                <span className="min-w-0 flex-1 truncate">{c.name}</span>
                <span className="text-[11px] text-fg-subtle">{c.id}</span>
              </button>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

/** Multi-select of channels with search; selected ones appear as removable chips. */
export function ChannelMultiPicker({ options, value, onChange, placeholder, disabled }: { options: ChannelOption[]; value: number[]; onChange: (ids: number[]) => void; placeholder: string; disabled?: boolean }) {
  const [query, setQuery] = useState('');
  const toggle = (id: number) => onChange(value.includes(id) ? value.filter((v) => v !== id) : [...value, id]);
  return (
    <div className="flex flex-col gap-2">
      <Popover>
        <PopoverTrigger asChild>
          <Button variant="secondary" disabled={disabled} iconRight={<ChevronsUpDown />} className="justify-between">
            {value.length > 0 ? t('{count} selected', { count: value.length }) : placeholder}
          </Button>
        </PopoverTrigger>
        <PopoverContent align="start" className="w-80 p-2">
          <OptionList options={options} isSelected={(id) => value.includes(id)} onPick={toggle} query={query} setQuery={setQuery} />
        </PopoverContent>
      </Popover>
      {value.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {value.map((id) => (
            <Badge key={id} tone="accent" className="gap-1 pr-1">
              <span className="max-w-48 truncate">{channelName(options, id)}</span>
              <button type="button" onClick={() => toggle(id)} aria-label={t('Remove {name}', { name: channelName(options, id) })} className="rounded p-0.5 hover:bg-accent-500/25" disabled={disabled}>
                <X className="size-3" />
              </button>
            </Badge>
          ))}
        </div>
      )}
    </div>
  );
}

/** Single channel selector with search. */
export function ChannelSinglePicker({ options, value, onChange, placeholder }: { options: ChannelOption[]; value: number | null; onChange: (id: number) => void; placeholder: string }) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button variant="secondary" iconRight={<ChevronsUpDown />} className="w-full justify-between">
          <span className="truncate">{value !== null ? channelName(options, value) : placeholder}</span>
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-80 p-2">
        <OptionList
          options={options}
          isSelected={(id) => id === value}
          onPick={(id) => {
            onChange(id);
            setOpen(false);
          }}
          query={query}
          setQuery={setQuery}
        />
      </PopoverContent>
    </Popover>
  );
}
