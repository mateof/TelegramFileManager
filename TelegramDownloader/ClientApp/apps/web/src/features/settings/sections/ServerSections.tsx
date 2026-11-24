import { StrmStreamingMode } from '@tfm/api-client';
import { Badge, Button, confirm, RadioGroup, Select, Slider, toast } from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, Cpu, MonitorSmartphone, Database, Film, RefreshCw, Timer, Trash2, TriangleAlert, Zap } from 'lucide-react';
import { useId } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { useRealtime } from '@/lib/realtime';
import { useUi } from '@/stores/ui';
import { CHUNK_SIZES_KB, maxMemorySplitGb, telegramMaxGb } from '../model/configForm';
import { settingsKeys } from '../model/useSettingsForm';
import { NumberRow, SettingRow, SettingsGroup, SwitchRow } from '../components/SettingRow';
import type { FormSectionProps } from './types';

export function GeneralSection({ form, values: v }: FormSectionProps) {
  const e = form.errors;
  return (
    <>
      <SettingsGroup title={t('Web interface')} icon={<MonitorSmartphone />}>
        <SwitchRow id="defaultWebUi" checked={v.defaultWebUi === 'app'} onChange={(b) => form.setField('defaultWebUi', b ? 'app' : 'classic')} />
      </SettingsGroup>
      <SettingsGroup title={t('Downloads')} icon={<Zap />}>
        <NumberRow id="maxSimultaneousDownloads" value={v.maxSimultaneousDownloads} onChange={(n) => form.setField('maxSimultaneousDownloads', n)} min={1} max={3} error={e.maxSimultaneousDownloads} />
        <NumberRow id="parallelTransfers" value={v.parallelTransfers} onChange={(n) => form.setField('parallelTransfers', n)} min={1} max={16} error={e.parallelTransfers} />
      </SettingsGroup>
      <SettingsGroup title={t('Advanced')} icon={<Timer />}>
        <NumberRow id="timeSleepBetweenTransactions" value={v.timeSleepBetweenTransactions} onChange={(n) => form.setField('timeSleepBetweenTransactions', n)} min={0} unit="ms" error={e.timeSleepBetweenTransactions} />
        <SwitchRow id="shouldShowLogInTerminal" checked={v.shouldShowLogInTerminal} onChange={(b) => form.setField('shouldShowLogInTerminal', b)} />
      </SettingsGroup>
    </>
  );
}

export function ConnectionsSection({ form, values: v }: FormSectionProps) {
  const e = form.errors;
  const multi = v.enableMultiConnectionDownloads;
  const chunkId = useId();
  return (
    <>
      <SettingsGroup title={t('Multi-connection downloads')} icon={<Zap />}>
        <SwitchRow
          id="enableMultiConnectionDownloads"
          checked={multi}
          onChange={(b) => form.setField('enableMultiConnectionDownloads', b)}
          badge={<Badge tone="warning">{t('Experimental')}</Badge>}
        />
        {multi && (
          <>
            <NumberRow id="downloadConnections" value={v.downloadConnections} onChange={(n) => form.setField('downloadConnections', n)} min={2} max={8} error={e.downloadConnections} />
            <SettingRow id="multiConnectionPartSizeKB" controlId={chunkId} error={e.multiConnectionPartSizeKB}>
              <Select
                id={chunkId}
                value={String(v.multiConnectionPartSizeKB)}
                onValueChange={(s) => form.setField('multiConnectionPartSizeKB', Number(s))}
                options={CHUNK_SIZES_KB.map((kb) => ({ value: String(kb), label: `${kb} KB` }))}
                className="w-36"
              />
            </SettingRow>
            <NumberRow id="multiConnectionBlockSizeMB" value={v.multiConnectionBlockSizeMB} onChange={(n) => form.setField('multiConnectionBlockSizeMB', n)} min={1} max={16} unit="MB" error={e.multiConnectionBlockSizeMB} />
            <NumberRow id="multiConnectionMinFileSizeMB" value={v.multiConnectionMinFileSizeMB} onChange={(n) => form.setField('multiConnectionMinFileSizeMB', n)} min={1} max={1024} unit="MB" error={e.multiConnectionMinFileSizeMB} />
          </>
        )}
      </SettingsGroup>
      <SettingsGroup title={t('Preload')} icon={<Database />}>
        <NumberRow id="maxPreloadFileSizeInMb" value={v.maxPreloadFileSizeInMb} onChange={(n) => form.setField('maxPreloadFileSizeInMb', n)} min={0} max={10000} unit="MB" error={e.maxPreloadFileSizeInMb} />
      </SettingsGroup>
    </>
  );
}

const NATIVE_FORMATS = ['.mp4', '.webm', '.ogg', '.ogv'];
const TRANSCODED_FORMATS = ['.mkv', '.avi', '.wmv', '.mov', '.flv', '.ts', '.m2ts', '.vob', '.3gp'];

export function StreamingSection({ form, values: v }: FormSectionProps) {
  const ffmpeg = useQuery({ queryKey: settingsKeys.ffmpeg, queryFn: ({ signal }) => api.system.ffmpeg(false, signal), staleTime: 5 * 60_000 });
  const qc = useQueryClient();
  const recheck = useMutation({
    mutationFn: () => api.system.ffmpeg(true),
    onSuccess: (data) => qc.setQueryData(settingsKeys.ffmpeg, data)
  });
  const available = ffmpeg.data?.available ?? false;
  return (
    <>
      <SettingsGroup title={t('STRM playback')} icon={<Film />}>
        <SettingRow id="strmStreamingMode" stacked>
          <RadioGroup
            value={v.strmStreamingMode}
            onValueChange={(mode) => form.setField('strmStreamingMode', mode)}
            options={[
              { value: StrmStreamingMode.DirectStream, label: t('Direct streaming'), description: t('Plays instantly; chunks are fetched from Telegram on demand and nothing is stored on disk. Every play downloads again from Telegram.') },
              {
                value: StrmStreamingMode.ProgressiveCache,
                label: (
                  <span className="inline-flex items-center gap-2">
                    {t('Progressive cache')} <Badge tone="success">{t('Recommended')}</Badge>
                  </span>
                ),
                description: t('Plays instantly while the file is downloaded to the local cache in the background. Next plays are served from disk.')
              },
              { value: StrmStreamingMode.Preload, label: t('Full preload'), description: t('Downloads the whole file before playback starts (legacy behaviour).') }
            ]}
          />
        </SettingRow>
      </SettingsGroup>
      <SettingsGroup
        title={t('Video transcoding')}
        icon={<Cpu />}
        actions={
          <Button variant="ghost" size="xs" icon={<RefreshCw />} loading={recheck.isPending} onClick={() => recheck.mutate()}>
            {t('Check again')}
          </Button>
        }
      >
        <SwitchRow
          id="enableVideoTranscoding"
          checked={v.enableVideoTranscoding}
          onChange={(b) => form.setField('enableVideoTranscoding', b)}
          disabled={!available && !v.enableVideoTranscoding}
          badge={
            ffmpeg.isPending ? null : available ? (
              <Badge tone="success" dot>
                {t('FFmpeg {version}', { version: ffmpeg.data?.version ?? '' })}
              </Badge>
            ) : (
              <Badge tone="danger" dot>
                {t('FFmpeg not found')}
              </Badge>
            )
          }
        />
        <div className="grid gap-4 px-5 py-4 text-xs text-fg-muted sm:grid-cols-2">
          <div>
            <p className="mb-2 font-medium text-fg">{t('Always playable')}</p>
            <div className="flex flex-wrap gap-1.5">
              {NATIVE_FORMATS.map((f) => (
                <Badge key={f} tone="success">
                  {f}
                </Badge>
              ))}
            </div>
          </div>
          <div>
            <p className="mb-2 font-medium text-fg">{t('Need transcoding (FFmpeg)')}</p>
            <div className="flex flex-wrap gap-1.5">
              {TRANSCODED_FORMATS.map((f) => (
                <Badge key={f} tone={available ? 'info' : 'neutral'}>
                  {f}
                </Badge>
              ))}
            </div>
          </div>
          {!available && !ffmpeg.isPending && (
            <p className="sm:col-span-2">
              {t('FFmpeg must be installed and in the system PATH. The official Docker image already includes it; for manual installations get it from ffmpeg.org.')}
            </p>
          )}
        </div>
      </SettingsGroup>
    </>
  );
}

export function UploadsSection({ form, values: v }: FormSectionProps) {
  const e = form.errors;
  const maxGb = telegramMaxGb(form.isPremium);
  const memoryMax = maxMemorySplitGb(v.splitSize, form.isPremium);
  return (
    <>
      <SettingsGroup title={t('Uploads')} icon={<Zap />}>
        <NumberRow id="maxImageUploadSizeInMb" value={v.maxImageUploadSizeInMb} onChange={(n) => form.setField('maxImageUploadSizeInMb', n)} min={0} max={10} unit="MB" error={e.maxImageUploadSizeInMb} />
        <SettingRow
          id="splitSize"
          error={e.splitSize}
          badge={<Badge tone={form.isPremium ? 'accent' : 'neutral'}>{form.isPremium ? t('Premium: up to 4 GB') : t('Up to 2 GB')}</Badge>}
        >
          <div className="flex w-56 items-center gap-3">
            <Slider
              value={[Math.min(v.splitSize, maxGb)]}
              min={0}
              max={maxGb}
              step={1}
              onValueChange={([n]) => form.setField('splitSize', n ?? 0)}
              thumbLabels={[t('Split size')]}
              className="flex-1"
            />
            <span className="w-14 text-right text-sm tabular-nums text-fg">{v.splitSize === 0 ? t('Off') : `${v.splitSize} GB`}</span>
          </div>
        </SettingRow>
        <SwitchRow
          id="enableMemorySplitUpload"
          checked={v.enableMemorySplitUpload}
          onChange={(b) => form.setField('enableMemorySplitUpload', b)}
          disabled={v.splitSize === 0 && !v.enableMemorySplitUpload}
          error={e.enableMemorySplitUpload}
          extra={v.splitSize === 0 ? <p className="mt-1 text-[11px] text-warning">{t('Requires a split size above 0')}</p> : undefined}
        />
        {v.enableMemorySplitUpload && v.splitSize > 0 && (
          <SettingRow id="memorySplitSizeGB" error={e.memorySplitSizeGB}>
            <div className="flex w-56 items-center gap-3">
              <Slider
                value={[Math.min(Math.max(v.memorySplitSizeGB, 1), memoryMax)]}
                min={1}
                max={memoryMax}
                step={1}
                disabled={memoryMax <= 1}
                onValueChange={([n]) => form.setField('memorySplitSizeGB', n ?? 1)}
                thumbLabels={[t('Memory chunk size')]}
                className="flex-1"
              />
              <span className="w-14 text-right text-sm tabular-nums text-fg">{v.memorySplitSizeGB} GB</span>
            </div>
          </SettingRow>
        )}
        <SwitchRow id="shouldShowCaptionPath" checked={v.shouldShowCaptionPath} onChange={(b) => form.setField('shouldShowCaptionPath', b)} />
        <SwitchRow id="checkHash" checked={v.checkHash} onChange={(b) => form.setField('checkHash', b)} />
      </SettingsGroup>
    </>
  );
}

export function FileManagerSection({ form, values: v }: FormSectionProps) {
  const singleClickFolders = useUi((s) => s.singleClickFolders);
  const setSingleClickFolders = useUi((s) => s.setSingleClickFolders);
  return (
    <SettingsGroup title={t('File manager')} description={t('Options of the file manager of the web interface.')}>
      <SwitchRow id="useMobileFileManagerAlways" checked={v.useMobileFileManagerAlways} onChange={(b) => form.setField('useMobileFileManagerAlways', b)} />
      <SwitchRow id="hasFileManagerVirtualScroll" checked={v.hasFileManagerVirtualScroll} onChange={(b) => form.setField('hasFileManagerVirtualScroll', b)} />
      <SwitchRow id="shouldShowPaginatedFileChannel" checked={v.shouldShowPaginatedFileChannel} onChange={(b) => form.setField('shouldShowPaginatedFileChannel', b)} />
      <SwitchRow id="singleClickFolders" checked={singleClickFolders} onChange={setSingleClickFolders} />
    </SettingsGroup>
  );
}

export function NotificationsSection({ form, values: v }: FormSectionProps) {
  return (
    <SettingsGroup title={t('Notifications')}>
      <SwitchRow id="shouldNotify" checked={v.shouldNotify} onChange={(b) => form.setField('shouldNotify', b)} />
      <p className="px-5 py-3 text-xs text-fg-muted">{t('The notifications appear in the bell of the top bar and as toasts in every open browser. Errors are always shown.')}</p>
    </SettingsGroup>
  );
}

export function TasksSection({ form, values: v }: FormSectionProps) {
  const e = form.errors;
  const qc = useQueryClient();
  const summary = useRealtime((s) => s.transferSummary);
  const pending = summary?.queuedDownloads ?? 0;
  const persisted = useQuery({
    queryKey: settingsKeys.persisted,
    queryFn: async ({ signal }) => (await api.transfers.persisted({ page: 1, pageSize: 1 }, signal)).page.totalItems
  });
  const clearPending = useMutation({
    mutationFn: () => api.transfers.clearQueue('downloads'),
    onSuccess: () => toast.success(t('Pending downloads cleared')),
    onError: (error) => toast.error(t('Could not clear the queue'), { description: errorMessage(error) })
  });
  const clearPersisted = useMutation({
    mutationFn: () => api.transfers.clearPersisted(),
    onSuccess: () => {
      toast.success(t('Persisted tasks cleared'));
      void qc.invalidateQueries({ queryKey: settingsKeys.persisted });
    },
    onError: (error) => toast.error(t('Could not clear the persisted tasks'), { description: errorMessage(error) })
  });
  return (
    <>
      <SettingsGroup title={t('Task persistence')} icon={<Database />}>
        <SwitchRow id="enableTaskPersistence" checked={v.enableTaskPersistence} onChange={(b) => form.setField('enableTaskPersistence', b)} />
        <SwitchRow id="autoResumeOnStartup" checked={v.autoResumeOnStartup} onChange={(b) => form.setField('autoResumeOnStartup', b)} disabled={!v.enableTaskPersistence} />
        <NumberRow id="taskPersistenceDebounceSeconds" value={v.taskPersistenceDebounceSeconds} onChange={(n) => form.setField('taskPersistenceDebounceSeconds', n)} min={1} max={60} unit={t('sec')} error={e.taskPersistenceDebounceSeconds} disabled={!v.enableTaskPersistence} />
        <NumberRow id="staleTaskCleanupDays" value={v.staleTaskCleanupDays} onChange={(n) => form.setField('staleTaskCleanupDays', n)} min={1} max={30} unit={t('days')} error={e.staleTaskCleanupDays} disabled={!v.enableTaskPersistence} />
      </SettingsGroup>
      <SettingsGroup title={t('Queues')} icon={<TriangleAlert />}>
        <SettingRow id="pendingDownloads" badge={<Badge tone={pending > 0 ? 'warning' : 'neutral'}>{pending}</Badge>}>
          <Button
            variant="outline"
            size="sm"
            icon={<Trash2 />}
            disabled={pending === 0}
            loading={clearPending.isPending}
            onClick={async () => {
              if (await confirm({ title: t('Clear the pending downloads?'), description: t('Downloads that have not started are removed from the queue.'), destructive: true, confirmLabel: t('Clear') })) clearPending.mutate();
            }}
          >
            {t('Clear')}
          </Button>
        </SettingRow>
        <SettingRow id="persistedTasks" badge={<Badge tone={(persisted.data ?? 0) > 0 ? 'warning' : 'neutral'}>{persisted.data ?? '–'}</Badge>}>
          <Button
            variant="outline"
            size="sm"
            icon={<Trash2 />}
            disabled={!persisted.data}
            loading={clearPersisted.isPending}
            onClick={async () => {
              if (await confirm({ title: t('Delete every persisted task?'), description: t('They will not be resumed after a restart.'), destructive: true, confirmLabel: t('Delete') })) clearPersisted.mutate();
            }}
          >
            {t('Clear')}
          </Button>
        </SettingRow>
        {persisted.data === 0 && pending === 0 && (
          <p className="flex items-center gap-2 px-5 py-3 text-xs text-fg-muted">
            <CheckCircle2 className="size-4 text-success" /> {t('Nothing pending.')}
          </p>
        )}
      </SettingsGroup>
    </>
  );
}
