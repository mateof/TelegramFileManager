import type { ApiLibraryScanStateDto, LibraryFolderRule, LibraryFolderRuleKind } from '@tfm/api-client';
import { isApiError } from '@tfm/api-client';
import { Badge, Button, cn, IconButton, Input, Progress, RadioGroup, Select, Skeleton, Switch, toast } from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowUpRight, Clapperboard, Eraser, FolderTree, KeyRound, ListFilter, Plus, Radar, RotateCcw, Search, Square, Trash2 } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { useEventsMessage } from '@/lib/realtime';
import { ChannelMultiPicker, ChannelSinglePicker } from '../components/ChannelPicker';
import { channelName, useChannelOptions, type ChannelOption } from '../components/channelOptions';
import { NumberInput, SettingRow, SettingsGroup, SwitchRow } from '../components/SettingRow';
import { normalizeFolderPath, type ProviderDraft } from '../model/configForm';
import { settingsKeys } from '../model/useSettingsForm';
import type { FormSectionProps } from './types';

function kindLabel(kind: string): string {
  if (kind === 'series') return t('Series');
  if (kind === 'movie') return t('Movies');
  if (kind === 'ignore') return t('Ignore');
  return kind;
}

function Providers({ form, values: v }: FormSectionProps) {
  const infos = form.config.data?.libraryProviders ?? [];
  const update = (id: string, patch: Partial<ProviderDraft>) =>
    form.setField(
      'libraryProviders',
      v.libraryProviders.map((p) => (p.id === id ? { ...p, ...patch } : p))
    );
  return (
    <SettingRow id="libraryProviders" stacked error={form.errors.libraryProviders}>
      <div className="flex flex-col gap-3">
        {v.libraryProviders.map((p) => {
          const info = infos.find((i) => i.id === p.id);
          if (!info) return null;
          const keyCleared = p.apiKey === '';
          return (
            <div key={p.id} className={cn('rounded-xl border border-border p-4 transition-opacity', !p.enabled && 'opacity-70')}>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 text-sm font-semibold text-fg">
                    {info.name}
                    {info.hasKey && !keyCleared ? <Badge tone="success" dot>{t('Key set')}</Badge> : info.requiresKey ? <Badge tone="warning" dot>{t('Needs a key')}</Badge> : null}
                  </p>
                  <p className="mt-0.5 text-xs text-fg-muted">
                    {info.supportsLanguage ? t('Localised titles, posters, backdrops and episodes.') : t('IMDb ratings and English plots; its ids are IMDb ids.')}
                  </p>
                  <p className="mt-1 flex flex-wrap gap-3 text-xs">
                    <a href={info.website} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-0.5 text-accent-400 hover:text-accent-300">
                      {info.website.replace(/^https?:\/\//, '')} <ArrowUpRight className="size-3" />
                    </a>
                    {info.keyUrl && (
                      <a href={info.keyUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-0.5 text-accent-400 hover:text-accent-300">
                        {t('Get an API key')} <ArrowUpRight className="size-3" />
                      </a>
                    )}
                  </p>
                </div>
                <Switch label={t('Enabled')} checked={p.enabled} onCheckedChange={(b) => update(p.id, { enabled: b })} />
              </div>
              <div className="mt-3 flex flex-wrap items-end gap-3">
                <label className="flex min-w-56 flex-1 flex-col gap-1 text-xs text-fg-muted">
                  {t('API key')}
                  <div className="flex items-center gap-1">
                    <Input
                      type="password"
                      autoComplete="new-password"
                      leading={<KeyRound />}
                      placeholder={keyCleared ? t('Will be cleared') : (info.apiKeyMasked ?? t('API key'))}
                      value={p.apiKey ?? ''}
                      onChange={(e) => update(p.id, { apiKey: e.target.value === '' ? null : e.target.value })}
                      className="flex-1"
                    />
                    {info.hasKey && p.apiKey === null && (
                      <IconButton label={t('Clear the key')} variant="ghost" size="sm" onClick={() => update(p.id, { apiKey: '' })}>
                        <Eraser />
                      </IconButton>
                    )}
                    {p.apiKey !== null && (
                      <IconButton label={t('Keep the current key')} variant="ghost" size="sm" onClick={() => update(p.id, { apiKey: null })}>
                        <RotateCcw />
                      </IconButton>
                    )}
                  </div>
                </label>
                <label className="flex flex-col gap-1 text-xs text-fg-muted">
                  {t('Priority')}
                  <NumberInput value={p.priority} onChange={(n) => update(p.id, { priority: n })} min={0} max={100} className="w-24" />
                </label>
              </div>
            </div>
          );
        })}
      </div>
    </SettingRow>
  );
}

function FolderList({ channelId, value, onChange }: { channelId: number; value: string; onChange: (path: string) => void }) {
  const [query, setQuery] = useState('');
  const folders = useQuery({
    queryKey: settingsKeys.channelFolders(channelId),
    queryFn: ({ signal }) => api.library.channelFolders(channelId, signal),
    staleTime: 60_000
  });
  const list = useMemo(() => {
    const all = ['/', ...(folders.data ?? []).filter((f) => f !== '/')];
    const q = query.trim().toLowerCase();
    return (q ? all.filter((f) => f.toLowerCase().includes(q)) : all).slice(0, 300);
  }, [folders.data, query]);

  if (folders.isPending) return <Skeleton className="h-32" />;
  if (folders.isError) return <p className="text-xs text-danger">{errorMessage(folders.error)}</p>;
  return (
    <div className="flex flex-col gap-2">
      <Input inputSize="sm" leading={<Search />} placeholder={t('Search folders...')} value={query} onChange={(e) => setQuery(e.target.value)} aria-label={t('Search folders...')} />
      <ul role="listbox" aria-label={t('Folder')} className="max-h-48 overflow-y-auto rounded-lg border border-border">
        {list.map((f) => (
          <li key={f} role="option" aria-selected={f === value}>
            <button
              type="button"
              onClick={() => onChange(f)}
              className={cn('flex w-full items-center gap-2 px-3 py-1.5 text-left font-mono text-xs hover:bg-surface-2', f === value && 'bg-primary-soft text-accent-300')}
            >
              <FolderTree className="size-3.5 shrink-0 opacity-60" />
              <span className="truncate">{f === '/' ? t('/ (whole channel)') : f}</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

function FolderRules({ form, values: v, options }: FormSectionProps & { options: ChannelOption[] }) {
  const [channelId, setChannelId] = useState<number | null>(null);
  const [path, setPath] = useState('/');
  const [kind, setKind] = useState<LibraryFolderRuleKind>('series');
  const rules = v.libraryFolderRules;
  const kindId = useId();
  const sorted = [...rules].sort((a, b) => channelName(options, a.channelId).localeCompare(channelName(options, b.channelId)) || a.path.localeCompare(b.path));

  const add = () => {
    if (channelId === null) return;
    const rule: LibraryFolderRule = { channelId, path: normalizeFolderPath(path), kind };
    const others = rules.filter((r) => !(r.channelId === rule.channelId && normalizeFolderPath(r.path) === rule.path));
    form.setField('libraryFolderRules', [...others, rule]);
    setPath('/');
  };
  const remove = (rule: LibraryFolderRule) => form.setField('libraryFolderRules', rules.filter((r) => r !== rule));

  return (
    <SettingRow id="libraryFolderRules" stacked error={form.errors.libraryFolderRules}>
      <div className="flex flex-col gap-4">
        {sorted.length > 0 ? (
          <div className="overflow-x-auto rounded-xl border border-border">
            <table className="w-full text-sm">
              <thead className="bg-surface-2 text-left text-xs text-fg-muted">
                <tr>
                  <th className="px-3 py-2 font-medium">{t('Channel')}</th>
                  <th className="px-3 py-2 font-medium">{t('Folder')}</th>
                  <th className="px-3 py-2 font-medium">{t('Kind')}</th>
                  <th className="w-10 px-3 py-2">
                    <span className="sr-only">{t('Actions')}</span>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {sorted.map((r) => (
                  <tr key={`${r.channelId}|${r.path}`}>
                    <td className="max-w-48 truncate px-3 py-2 text-fg" title={String(r.channelId)}>
                      {channelName(options, r.channelId)}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs text-fg">{r.path}</td>
                    <td className="px-3 py-2">
                      <Badge tone={r.kind === 'ignore' ? 'neutral' : r.kind === 'movie' ? 'info' : 'accent'}>{kindLabel(r.kind)}</Badge>
                    </td>
                    <td className="px-2 py-1">
                      <IconButton label={t('Remove')} size="sm" variant="ghost" onClick={() => remove(r)}>
                        <Trash2 />
                      </IconButton>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-xs text-fg-subtle">{t('No folder rules yet.')}</p>
        )}

        <div className="grid gap-3 rounded-xl border border-dashed border-border p-3 md:grid-cols-[1fr_1.4fr_auto]">
          <div className="flex flex-col gap-1.5">
            <span className="text-xs font-medium text-fg-muted">{t('1. Channel')}</span>
            <ChannelSinglePicker
              options={options}
              value={channelId}
              onChange={(id) => {
                setChannelId(id);
                setPath('/');
              }}
              placeholder={t('Pick a channel')}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <span className="text-xs font-medium text-fg-muted">{t('2. Folder')}</span>
            {channelId === null ? <p className="py-2 text-xs text-fg-subtle">{t('Pick a channel first.')}</p> : <FolderList channelId={channelId} value={path} onChange={setPath} />}
          </div>
          <div className="flex flex-col gap-1.5">
            <label htmlFor={kindId} className="text-xs font-medium text-fg-muted">
              {t('3. Kind')}
            </label>
            <Select<LibraryFolderRuleKind>
              id={kindId}
              value={kind}
              onValueChange={setKind}
              options={[
                { value: 'series', label: t('Series') },
                { value: 'movie', label: t('Movies') },
                { value: 'ignore', label: t('Ignore') }
              ]}
              className="w-36"
            />
            <Button icon={<Plus />} disabled={channelId === null} onClick={add} className="mt-auto">
              {t('Add rule')}
            </Button>
          </div>
        </div>
        <p className="text-[11px] text-fg-subtle">{t('Rules are stored when you save the settings.')}</p>
      </div>
    </SettingRow>
  );
}

function ScanStatus({ form }: { form: FormSectionProps['form'] }) {
  const qc = useQueryClient();
  const scan = useQuery({ queryKey: settingsKeys.libraryScan, queryFn: ({ signal }) => api.library.scanState(signal) });
  useEventsMessage('LibraryScan', (e) => {
    if (e.progress) qc.setQueryData<ApiLibraryScanStateDto | null>(settingsKeys.libraryScan, e.progress);
    else if (!e.running) void qc.invalidateQueries({ queryKey: settingsKeys.libraryScan });
  });
  const start = useMutation({
    mutationFn: (force: boolean) => api.library.scan({ force }),
    onSuccess: () => {
      toast.success(t('Library scan started'));
      void qc.invalidateQueries({ queryKey: settingsKeys.libraryScan });
    },
    onError: (error) =>
      toast.error(isApiError(error) && error.code === 'already_running' ? t('A scan is already running') : t('Could not start the scan'), { description: errorMessage(error) })
  });
  const cancel = useMutation({
    mutationFn: () => api.library.cancelScan(),
    onSuccess: () => toast(t('Cancelling the scan...')),
    onError: (error) => toast.error(t('Could not cancel the scan'), { description: errorMessage(error) })
  });

  const s = scan.data;
  const running = s?.running ?? false;
  const percent = s?.filesToProcess ? ((s.filesProcessed ?? 0) / s.filesToProcess) * 100 : s && s.filesToProcess === undefined && s.channelsTotal > 0 ? (s.channelsScanned / s.channelsTotal) * 100 : undefined;
  const run = async (force: boolean) => {
    if (form.isDirty && !(await form.submit())) return;
    start.mutate(force);
  };

  return (
    <SettingRow id="libraryScan" stacked badge={running ? <Badge tone="info" dot>{t('Running')}</Badge> : undefined}>
      <div className="flex flex-col gap-3">
        {scan.isPending ? (
          <Skeleton className="h-10" />
        ) : !s ? (
          <p className="text-sm text-fg-muted">{t('Never scanned.')}</p>
        ) : running ? (
          <>
            <p className="text-sm text-fg">
              {t('Scanning {done}/{total} channels (current: {current}) — {matched} matched, {review} to review, {unmatched} unmatched.', {
                done: s.channelsScanned,
                total: s.channelsTotal,
                current: s.currentChannel ?? '–',
                matched: s.matched,
                review: s.review,
                unmatched: s.unmatched
              })}
            </p>
            <Progress value={percent} aria-label={t('Library scan')} />
          </>
        ) : (
          <p className="text-sm text-fg-muted">
            {t('Last scan {date}: {seen} files seen, {new} new, {matched} matched, {review} to review, {unmatched} unmatched.', {
              date: s.finishedAt ? new Date(s.finishedAt).toLocaleString(getLanguage()) : '–',
              seen: s.filesSeen,
              new: s.filesNew,
              matched: s.matched,
              review: s.review,
              unmatched: s.unmatched
            })}
            {s.cancelled && ` (${t('cancelled')})`}
          </p>
        )}
        {s?.error && <p className="text-sm text-danger">{s.error}</p>}
        <div className="flex flex-wrap gap-2">
          {running ? (
            <Button variant="outline" icon={<Square />} loading={cancel.isPending} onClick={() => cancel.mutate()}>
              {t('Cancel scan')}
            </Button>
          ) : (
            <>
              <Button icon={<Radar />} loading={start.isPending} disabled={!form.values?.libraryEnabled} onClick={() => void run(false)} title={t('Save the settings and identify new files')}>
                {form.isDirty ? t('Save & scan') : t('Scan now')}
              </Button>
              <Button variant="secondary" icon={<RotateCcw />} disabled={start.isPending || !form.values?.libraryEnabled} onClick={() => void run(true)} title={t('Identify every file again (manual fixes are kept)')}>
                {t('Full rescan')}
              </Button>
            </>
          )}
        </div>
        {!form.values?.libraryEnabled && <p className="text-xs text-fg-subtle">{t('Enable the library to scan.')}</p>}
      </div>
    </SettingRow>
  );
}

export function LibrarySection(props: FormSectionProps) {
  const { form, values: v } = props;
  const e = form.errors;
  const { options } = useChannelOptions(true);
  const [onlySelected, setOnlySelected] = useState(v.libraryIncludedChannels.length > 0);
  const langId = useId();
  const thresholdId = useId();

  return (
    <>
      <SettingsGroup title={t('Media library')} icon={<Clapperboard />}>
        <SwitchRow id="libraryEnabled" checked={v.libraryEnabled} onChange={(b) => form.setField('libraryEnabled', b)} />
        <Providers {...props} />
        <SettingRow id="libraryLanguage" controlId={langId} error={e.libraryLanguage}>
          <Input id={langId} value={v.libraryLanguage} onChange={(ev) => form.setField('libraryLanguage', ev.target.value)} className="w-32" placeholder="es-ES" spellCheck={false} />
        </SettingRow>
        <SwitchRow id="libraryAutoScan" checked={v.libraryAutoScan} onChange={(b) => form.setField('libraryAutoScan', b)} />
        <SettingRow id="libraryWatchedThreshold" controlId={thresholdId} error={e.libraryWatchedThreshold}>
          <NumberInput id={thresholdId} value={v.libraryWatchedThreshold} onChange={(n) => form.setField('libraryWatchedThreshold', n)} min={0.5} max={1} step={0.01} invalid={!!e.libraryWatchedThreshold} />
        </SettingRow>
      </SettingsGroup>

      <SettingsGroup title={t('Channels')} icon={<ListFilter />}>
        <SettingRow id="libraryChannels" stacked>
          <div className="flex flex-col gap-4">
            <RadioGroup
              value={onlySelected ? 'selected' : 'all'}
              onValueChange={(mode) => {
                setOnlySelected(mode === 'selected');
                if (mode === 'all') form.setField('libraryIncludedChannels', []);
              }}
              options={[
                { value: 'all', label: t('All indexed channels') },
                { value: 'selected', label: t('Only the selected channels') }
              ]}
              orientation="horizontal"
            />
            {onlySelected && (
              <div className="flex flex-col gap-1.5">
                <p className="text-xs font-medium text-fg">{t('Included channels')}</p>
                <p className="text-xs text-fg-muted">{t('Only these channels are scanned. With none selected the library scans every indexed channel.')}</p>
                <ChannelMultiPicker options={options} value={v.libraryIncludedChannels} onChange={(ids) => form.setField('libraryIncludedChannels', ids)} placeholder={t('Choose channels to include...')} />
              </div>
            )}
            <div className="flex flex-col gap-1.5">
              <p className="text-xs font-medium text-fg">{t('Excluded channels')}</p>
              <p className="text-xs text-fg-muted">{t('Channels the library never scans, even when included above.')}</p>
              <ChannelMultiPicker options={options} value={v.libraryExcludedChannels} onChange={(ids) => form.setField('libraryExcludedChannels', ids)} placeholder={t('Choose channels to exclude...')} />
            </div>
          </div>
        </SettingRow>
        <FolderRules {...props} options={options} />
      </SettingsGroup>

      <SettingsGroup title={t('Library scan')} icon={<Radar />}>
        <ScanStatus form={form} />
      </SettingsGroup>
    </>
  );
}
