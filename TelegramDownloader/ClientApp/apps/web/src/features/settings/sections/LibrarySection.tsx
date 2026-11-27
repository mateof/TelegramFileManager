import type { ApiLibraryScanStateDto, ApiProviderCandidateDto, LibraryFolderRule, LibraryFolderRuleKind, LibraryScanRequest } from '@tfm/api-client';
import { isApiError } from '@tfm/api-client';
import { Badge, Button, Checkbox, cn, Dialog, DialogContent, IconButton, Input, Progress, RadioGroup, Select, Skeleton, Switch, toast } from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowUpRight, Clapperboard, Eraser, KeyRound, ListFilter, Plus, Radar, RotateCcw, Square, Trash2, Tv, X } from 'lucide-react';
import { useId, useState } from 'react';
import { CandidateSearch } from '@/features/library/components/CandidateSearch';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { useEventsMessage } from '@/lib/realtime';
import { ChannelMultiPicker, ChannelSinglePicker } from '../components/ChannelPicker';
import { channelName, useChannelOptions, type ChannelOption } from '../components/channelOptions';
import { FolderPicker } from '../components/FolderPicker';
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

/** The library scan: its state, and starting it (saving the settings first when there are changes). */
function useLibraryScan(form: FormSectionProps['form']) {
  const qc = useQueryClient();
  const scan = useQuery({ queryKey: settingsKeys.libraryScan, queryFn: ({ signal }) => api.library.scanState(signal) });
  useEventsMessage('LibraryScan', (e) => {
    if (e.progress) qc.setQueryData<ApiLibraryScanStateDto | null>(settingsKeys.libraryScan, e.progress);
    else if (!e.running) void qc.invalidateQueries({ queryKey: settingsKeys.libraryScan });
  });
  const start = useMutation({
    mutationFn: (request: LibraryScanRequest) => api.library.scan(request),
    onSuccess: () => {
      toast.success(t('Library scan started'));
      void qc.invalidateQueries({ queryKey: settingsKeys.libraryScan });
    },
    onError: (error) =>
      toast.error(isApiError(error) && error.code === 'already_running' ? t('A scan is already running') : t('Could not start the scan'), { description: errorMessage(error) })
  });
  return {
    state: scan.data,
    loading: scan.isPending,
    running: scan.data?.running ?? false,
    starting: start.isPending,
    enabled: !!form.values?.libraryEnabled,
    run: async (request: LibraryScanRequest) => {
      if (form.isDirty && !(await form.submit())) return;
      start.mutate(request);
    }
  };
}

type LibraryScan = ReturnType<typeof useLibraryScan>;

const ruleKey = (r: Pick<LibraryFolderRule, 'channelId' | 'path'>) => `${r.channelId}|${normalizeFolderPath(r.path)}`;

function FolderRules({ form, values: v, options, scan }: FormSectionProps & { options: ChannelOption[]; scan: LibraryScan }) {
  const [channelId, setChannelId] = useState<number | null>(null);
  const [path, setPath] = useState('/');
  const [kind, setKind] = useState<LibraryFolderRuleKind>('series');
  const [title, setTitle] = useState<ApiProviderCandidateDto | null>(null);
  const [picking, setPicking] = useState(false);
  const rules = v.libraryFolderRules;
  const kindId = useId();
  const sorted = [...rules].sort((a, b) => channelName(options, a.channelId).localeCompare(channelName(options, b.channelId)) || a.path.localeCompare(b.path));

  const add = () => {
    if (channelId === null) return;
    const rule: LibraryFolderRule = { channelId, path: normalizeFolderPath(path), kind, providerId: '' };
    if (kind === 'series' && title) Object.assign(rule, { provider: title.provider, providerId: title.providerId, title: title.title, year: title.year });
    const others = rules.filter((r) => !(r.channelId === rule.channelId && normalizeFolderPath(r.path) === rule.path));
    form.setField('libraryFolderRules', [...others, rule]);
    setPath('/');
    setTitle(null);
  };
  const [chosen, setChosen] = useState<ReadonlySet<string>>(() => new Set());
  const selected = sorted.filter((r) => chosen.has(ruleKey(r)));
  const choose = (key: string, on: boolean) => {
    const next = new Set(chosen);
    if (on) next.add(key);
    else next.delete(key);
    setChosen(next);
  };
  const canScan = scan.enabled && !scan.running && !scan.starting;
  const scanRules = (list: LibraryFolderRule[], force: boolean) => void scan.run({ folders: list.map((r) => ({ channelId: r.channelId, path: normalizeFolderPath(r.path) })), force });
  const remove = (rule: LibraryFolderRule) => form.setField('libraryFolderRules', rules.filter((r) => r !== rule));

  return (
    <SettingRow id="libraryFolderRules" stacked error={form.errors.libraryFolderRules}>
      <div className="flex flex-col gap-4">
        {sorted.length > 0 ? (
          <div className="overflow-x-auto rounded-xl border border-border">
            <table className="w-full text-sm">
              <thead className="bg-surface-2 text-left text-xs text-fg-muted">
                <tr>
                  <th className="w-8 py-2 pl-3">
                    <Checkbox
                      checked={selected.length === sorted.length ? true : selected.length > 0 ? 'indeterminate' : false}
                      onCheckedChange={(all) => setChosen(new Set(all === true ? sorted.map(ruleKey) : []))}
                      aria-label={t('Select all')}
                    />
                  </th>
                  <th className="px-3 py-2 font-medium max-sm:hidden">{t('Channel')}</th>
                  <th className="px-3 py-2 font-medium">{t('Folder')}</th>
                  <th className="px-3 py-2 font-medium">{t('Kind')}</th>
                  <th className="w-10 px-3 py-2">
                    <span className="sr-only">{t('Actions')}</span>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {sorted.map((r) => (
                  <tr key={`${r.channelId}|${r.path}`} className={cn(chosen.has(ruleKey(r)) && 'bg-primary-soft')}>
                    <td className="py-2 pl-3">
                      <Checkbox
                        checked={chosen.has(ruleKey(r))}
                        onCheckedChange={(on) => choose(ruleKey(r), on === true)}
                        aria-label={t('Select {name}', { name: `${channelName(options, r.channelId)} ${r.path}` })}
                      />
                    </td>
                    <td className="max-w-48 truncate px-3 py-2 text-fg max-sm:hidden" title={String(r.channelId)}>
                      {channelName(options, r.channelId)}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs text-fg">
                      <span className="block font-sans text-sm sm:hidden">{channelName(options, r.channelId)}</span>
                      <span className="break-words">{r.path}</span>
                    </td>
                    <td className="px-3 py-2">
                      <Badge tone={r.kind === 'ignore' ? 'neutral' : r.kind === 'movie' ? 'info' : 'accent'}>{kindLabel(r.kind)}</Badge>
                      {r.kind === 'series' && r.providerId && (
                        <span className="ml-2 text-xs text-fg">
                          {r.title ?? r.providerId}
                          {r.year ? ` (${r.year})` : ''}
                        </span>
                      )}
                    </td>
                    <td className="px-2 py-1">
                      <div className="flex justify-end">
                        <IconButton
                          label={form.isDirty ? t('Save & scan this folder') : t('Scan this folder')}
                          size="sm"
                          variant="ghost"
                          disabled={!canScan}
                          onClick={() => scanRules([r], false)}
                        >
                          <Radar />
                        </IconButton>
                        <IconButton label={t('Remove')} size="sm" variant="ghost" onClick={() => remove(r)}>
                          <Trash2 />
                        </IconButton>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-xs text-fg-subtle">{t('No folder rules yet.')}</p>
        )}
        {selected.length > 0 && (
          <div className="flex flex-wrap items-center gap-2">
            <span className="min-w-28 flex-1 text-sm text-fg-muted">{selected.length === 1 ? t('1 folder selected') : t('{count} folders selected', { count: selected.length })}</span>
            <Button size="sm" icon={<Radar />} disabled={!canScan} onClick={() => scanRules(selected, false)} title={t('Identify the new files of these folders')}>
              {form.isDirty ? t('Save & scan the selected') : t('Scan the selected')}
            </Button>
            <Button
              size="sm"
              variant="secondary"
              icon={<RotateCcw />}
              disabled={!canScan}
              onClick={() => scanRules(selected, true)}
              title={t('Identify every file of these folders again (manual fixes are kept)')}
            >
              {t('Full rescan')}
            </Button>
          </div>
        )}
        {sorted.length > 0 && scan.running && <p className="text-xs text-fg-muted">{t('A scan is running. Its progress is below.')}</p>}

        {/* minmax(0, …): the long folder names must not widen the grid past the card. */}
        <div className="grid min-w-0 gap-3 rounded-xl border border-dashed border-border p-3 lg:grid-cols-[minmax(0,13rem)_minmax(0,1fr)_minmax(0,12rem)]">
          <div className="flex min-w-0 flex-col gap-1.5">
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
          <div className="flex min-w-0 flex-col gap-1.5">
            <span className="text-xs font-medium text-fg-muted">{t('2. Folder')}</span>
            {channelId === null ? (
              <p className="py-2 text-xs text-fg-subtle">{t('Pick a channel first.')}</p>
            ) : (
              <FolderPicker key={channelId} channelId={channelId} value={path} onChange={setPath} />
            )}
          </div>
          <div className="flex min-w-0 flex-col gap-1.5">
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
              className="w-full"
            />
            {kind === 'series' && (
              <div className="flex min-w-0 items-center gap-1">
                <Button size="sm" variant="secondary" icon={<Tv />} onClick={() => setPicking(true)} className="min-w-0 flex-1" title={title?.title}>
                  <span className="truncate">{title ? title.title : t('Only one series...')}</span>
                </Button>
                {title && (
                  <IconButton size="sm" label={t('Any series')} onClick={() => setTitle(null)}>
                    <X />
                  </IconButton>
                )}
              </div>
            )}
            <Button icon={<Plus />} disabled={channelId === null} onClick={add} className="w-full lg:mt-auto">
              {t('Add rule')}
            </Button>
          </div>
        </div>
        <p className="text-[11px] text-fg-subtle">{t('Rules are stored when you save the settings.')}</p>
        <Dialog open={picking} onOpenChange={setPicking}>
          {picking && (
            <DialogContent size="lg" title={t('Series of the folder')} description={t('Every video of the folder will be an episode of the series you choose.')}>
              <CandidateSearch
                initialTitle={title?.title ?? path.split('/').filter(Boolean).at(-1) ?? ''}
                kind="series"
                onChoose={(c) => {
                  setTitle(c);
                  setPicking(false);
                }}
              />
            </DialogContent>
          )}
        </Dialog>
      </div>
    </SettingRow>
  );
}

function ScanStatus({ form, scan, options }: { form: FormSectionProps['form']; scan: LibraryScan; options: ChannelOption[] }) {
  const cancel = useMutation({
    mutationFn: () => api.library.cancelScan(),
    onSuccess: () => toast(t('Cancelling the scan...')),
    onError: (error) => toast.error(t('Could not cancel the scan'), { description: errorMessage(error) })
  });

  const s = scan.state;
  const running = scan.running;
  const percent = s?.filesToProcess ? ((s.filesProcessed ?? 0) / s.filesToProcess) * 100 : s && s.filesToProcess === undefined && s.channelsTotal > 0 ? (s.channelsScanned / s.channelsTotal) * 100 : undefined;
  const folders = s?.folders ?? [];

  return (
    <SettingRow id="libraryScan" stacked badge={running ? <Badge tone="info" dot>{t('Running')}</Badge> : undefined}>
      <div className="flex flex-col gap-3">
        {scan.loading ? (
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
        {folders.length > 0 && (
          <p className="text-xs text-fg-subtle">
            {t('Only:')} {folders.map((f) => `${channelName(options, f.channelId)} ${f.path}`).join(', ')}
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
              <Button icon={<Radar />} loading={scan.starting} disabled={!scan.enabled} onClick={() => void scan.run({ force: false })} title={t('Save the settings and identify new files')}>
                {form.isDirty ? t('Save & scan') : t('Scan now')}
              </Button>
              <Button variant="secondary" icon={<RotateCcw />} disabled={scan.starting || !scan.enabled} onClick={() => void scan.run({ force: true })} title={t('Identify every file again (manual fixes are kept)')}>
                {t('Full rescan')}
              </Button>
            </>
          )}
        </div>
        {!scan.enabled && <p className="text-xs text-fg-subtle">{t('Enable the library to scan.')}</p>}
      </div>
    </SettingRow>
  );
}

export function LibrarySection(props: FormSectionProps) {
  const { form, values: v } = props;
  const e = form.errors;
  const { options } = useChannelOptions(true);
  const scan = useLibraryScan(form);
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
        <FolderRules {...props} options={options} scan={scan} />
      </SettingsGroup>

      <SettingsGroup title={t('Library scan')} icon={<Radar />}>
        <ScanStatus form={form} scan={scan} options={options} />
      </SettingsGroup>
    </>
  );
}
