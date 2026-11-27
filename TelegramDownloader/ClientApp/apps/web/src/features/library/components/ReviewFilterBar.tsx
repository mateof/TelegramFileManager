import type { ApiLibraryFileFacetsDto, LibraryKind } from '@tfm/api-client';
import { Button, Select } from '@tfm/ui';
import { FilterX } from 'lucide-react';
import { t } from '@/i18n';
import { useChannels } from '@/shell/useChannels';
import type { ReviewFilters } from '../queries';

const ALL = 'all';
const ruleValue = (channelId: number, path: string) => `${channelId}|${path}`;

const withCount = (label: string, count: number | undefined) => (count === undefined ? label : `${label} (${count})`);

function ruleKindLabel(kind: string) {
  return kind === 'series' ? t('Series') : kind === 'movie' ? t('Movies') : kind === 'ignore' ? t('Ignore') : kind;
}

/**
 * Kind, channel and folder rule of the review queue, with how many files of the tab each
 * one keeps. Picking a rule picks its channel; another channel drops the rule.
 */
export function ReviewFilterBar({ facets, filters, onChange }: { facets: ApiLibraryFileFacetsDto; filters: ReviewFilters; onChange: (next: ReviewFilters) => void }) {
  const channels = useChannels();
  const nameOf = (id: number) => channels.data?.find((c) => c.id === id)?.name ?? t('Channel {id}', { id });

  const kindCount = (kind: LibraryKind) => facets.kinds.find((k) => k.value === kind)?.count ?? 0;
  const channelList = [...facets.channels];
  if (filters.channelId !== undefined && !channelList.some((c) => c.channelId === filters.channelId)) channelList.push({ channelId: filters.channelId, count: 0 });
  const ruleList: (ApiLibraryFileFacetsDto['rules'][number] | { channelId: number; path: string; kind: null; title: null; count: number })[] = facets.rules.filter(
    (r) => filters.channelId === undefined || r.channelId === filters.channelId
  );
  // The chosen rule stays listed when no file of the other filters is under it (its kind is unknown then)
  if (filters.channelId !== undefined && filters.rule && !ruleList.some((r) => r.path === filters.rule))
    ruleList.push({ channelId: filters.channelId, path: filters.rule, kind: null, title: null, count: 0 });
  const showRules = facets.rules.length > 0 || filters.rule !== undefined;
  const active = filters.kind !== undefined || filters.channelId !== undefined;

  return (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label={t('Filters')}>
      <Select<string>
        size="sm"
        aria-label={t('Kind')}
        value={filters.kind ?? ALL}
        onValueChange={(v) => onChange({ ...filters, kind: v === ALL ? undefined : (v as LibraryKind) })}
        className="w-full sm:w-44"
        options={[
          { value: ALL, label: withCount(t('All kinds'), facets.kinds.reduce((n, k) => n + k.count, 0)) },
          { value: 'series', label: withCount(t('Series'), kindCount('series')) },
          { value: 'movie', label: withCount(t('Movies'), kindCount('movie')) }
        ]}
      />
      <Select<string>
        size="sm"
        aria-label={t('Channel')}
        value={filters.channelId !== undefined ? String(filters.channelId) : ALL}
        onValueChange={(v) => onChange({ ...filters, channelId: v === ALL ? undefined : Number(v), rule: undefined })}
        className="w-full sm:w-56"
        options={[
          { value: ALL, label: withCount(t('All channels'), facets.channels.reduce((n, c) => n + c.count, 0)) },
          ...channelList.map((c) => ({ value: String(c.channelId), label: withCount(nameOf(c.channelId), c.count) }))
        ]}
      />
      {showRules && (
        <Select<string>
          size="sm"
          aria-label={t('Folder rule')}
          value={filters.channelId !== undefined && filters.rule ? ruleValue(filters.channelId, filters.rule) : ALL}
          onValueChange={(v) => {
            if (v === ALL) return onChange({ ...filters, rule: undefined });
            const at = v.indexOf('|');
            onChange({ ...filters, channelId: Number(v.slice(0, at)), rule: v.slice(at + 1) });
          }}
          className="w-full sm:w-72"
          options={[
            { value: ALL, label: t('All folder rules') },
            ...ruleList.map((r) => ({
              value: ruleValue(r.channelId, r.path),
              label: (
                <span className="flex min-w-0 items-baseline gap-1.5">
                  {filters.channelId === undefined && <span className="truncate text-fg-muted">{nameOf(r.channelId)} ·</span>}
                  <span className="truncate font-mono text-xs">{r.path}</span>
                  <span className="shrink-0 text-xs text-fg-subtle">
                    {r.title ?? (r.kind ? ruleKindLabel(r.kind) : '')} ({r.count})
                  </span>
                </span>
              )
            }))
          ]}
        />
      )}
      {active && (
        <Button size="sm" variant="ghost" icon={<FilterX />} onClick={() => onChange({})}>
          {t('Clear filters')}
        </Button>
      )}
    </div>
  );
}
