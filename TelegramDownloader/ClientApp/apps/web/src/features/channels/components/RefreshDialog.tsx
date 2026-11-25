import type { ApiChannelRefreshFilterProgressDto, RefreshChannelRequest } from '@tfm/api-client';
import { isApiError } from '@tfm/api-client';
import { Badge, Button, Checkbox, Dialog, DialogContent, Progress, Switch, toast } from '@tfm/ui';
import { useQuery } from '@tanstack/react-query';
import { CheckCircle2, RefreshCw, Square, TriangleAlert } from 'lucide-react';
import { useEffect, useState } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatRelative } from '@/lib/format';
import { getLanguage } from '@/i18n';
import { useRefreshProgress } from '../lib/useRefreshProgress';

type MediaKey = 'includeDocuments' | 'includeAudio' | 'includeVideo' | 'includePhotos';

const TYPE_LABELS: Record<string, () => string> = {
  documents: () => t('Documents'),
  audio: () => t('Audio'),
  video: () => t('Video'),
  photos: () => t('Photos')
};

/** "1:05" until `iso`, updated every second. */
function Countdown({ until }: { until: string }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  const left = Math.max(0, Math.ceil((new Date(until).getTime() - now) / 1000));
  return <>{`${Math.floor(left / 60)}:${String(left % 60).padStart(2, '0')}`}</>;
}

function FilterRow({ f }: { f: ApiChannelRefreshFilterProgressDto }) {
  const pct = f.done ? 100 : f.total ? Math.min(99, (f.fetched / f.total) * 100) : undefined;
  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center justify-between text-xs">
        <span className="font-medium text-fg">{TYPE_LABELS[f.type]?.() ?? f.type}</span>
        <span className="tabular-nums text-fg-muted">
          {f.pending ? t('Waiting for its turn') : f.done ? t('Done') : f.total !== null ? t('{fetched} of {total} messages', { fetched: f.fetched, total: f.total }) : t('{fetched} messages', { fetched: f.fetched })}
        </span>
      </div>
      <Progress value={f.pending ? 0 : pct} size="xs" tone={f.done ? 'success' : f.waitUntil ? 'warning' : 'accent'} aria-label={TYPE_LABELS[f.type]?.() ?? f.type} />
      {f.waitUntil && (
        <span className="text-xs text-warning">
          {t('Telegram asks to slow down. It goes on by itself in')} <Countdown until={f.waitUntil} />
        </span>
      )}
    </div>
  );
}

/** Starts a channel index refresh (media types, force) and shows its live progress with cancel. */
export function RefreshDialog({ channelId, open, onOpenChange }: { channelId: string; open: boolean; onOpenChange(open: boolean): void }) {
  const { progress, result, refetch } = useRefreshProgress(channelId);
  const [types, setTypes] = useState<Record<MediaKey, boolean>>({ includeDocuments: true, includeAudio: true, includeVideo: true, includePhotos: true });
  const [force, setForce] = useState(false);
  // Null until it is changed here: the setting decides
  const [oneByOne, setOneByOne] = useState<boolean | null>(null);
  const config = useQuery({ queryKey: ['settings', 'config'], queryFn: ({ signal }) => api.config.get(signal), staleTime: 30_000, enabled: open });
  const oneTypeAtATime = oneByOne ?? config.data?.refreshOneTypeAtATime ?? false;
  const [busy, setBusy] = useState(false);
  const anyType = Object.values(types).some(Boolean);

  const start = async () => {
    setBusy(true);
    try {
      const body: RefreshChannelRequest = { ...types, force, oneTypeAtATime: oneByOne ?? undefined };
      await api.channels.refresh(channelId, body);
      toast.success(t('Refresh started'));
      await refetch();
    } catch (error) {
      if (isApiError(error) && error.code === 'already_running') {
        toast.info(t('A refresh is already running'));
        await refetch();
      } else toast.error(t('Could not start the refresh'), { description: errorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  const cancel = async () => {
    try {
      await api.channels.cancelRefresh(channelId);
      await refetch();
    } catch (error) {
      toast.error(t('Could not cancel the refresh'), { description: errorMessage(error) });
    }
  };

  const options: [MediaKey, string][] = [
    ['includeDocuments', t('Documents')],
    ['includeAudio', t('Audio')],
    ['includeVideo', t('Video')],
    ['includePhotos', t('Photos')]
  ];

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        size="md"
        title={t('Refresh the channel index')}
        description={t('Scans Telegram for files that are not indexed yet.')}
        footer={
          progress ? (
            <>
              <Button variant="secondary" onClick={() => onOpenChange(false)}>
                {t('Close')}
              </Button>
              <Button variant="danger" icon={<Square />} disabled={progress.cancelRequested} onClick={() => void cancel()}>
                {progress.cancelRequested ? t('Canceling...') : t('Cancel refresh')}
              </Button>
            </>
          ) : (
            <>
              <Button variant="secondary" onClick={() => onOpenChange(false)}>
                {t('Close')}
              </Button>
              <Button icon={<RefreshCw />} loading={busy} disabled={!anyType} onClick={() => void start()}>
                {t('Start refresh')}
              </Button>
            </>
          )
        }
      >
        {progress ? (
          <div className="flex flex-col gap-4" role="status" aria-live="polite">
            <div className="flex items-center gap-2">
              <Badge tone="accent" dot>
                {progress.phase === 'indexing' ? t('Indexing') : t('Searching Telegram')}
              </Badge>
              {progress.cancelRequested && <Badge tone="warning">{t('Cancel requested')}</Badge>}
            </div>
            <div className="flex flex-col gap-3">
              {progress.filters.map((f) => (
                <FilterRow key={f.type} f={f} />
              ))}
            </div>
            {progress.phase === 'indexing' && (
              <div className="flex flex-col gap-1">
                <div className="flex justify-between text-xs">
                  <span className="font-medium text-fg">{t('New files')}</span>
                  <span className="tabular-nums text-fg-muted">
                    {progress.indexed} / {progress.newFiles}
                  </span>
                </div>
                <Progress value={progress.newFiles ? (progress.indexed / progress.newFiles) * 100 : undefined} size="xs" aria-label={t('New files')} />
              </div>
            )}
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            <div className="grid grid-cols-2 gap-3">
              {options.map(([key, label]) => (
                <Checkbox key={key} label={label} checked={types[key]} onCheckedChange={(v) => setTypes((s) => ({ ...s, [key]: v === true }))} />
              ))}
            </div>
            <Switch label={t('Full rescan')} description={t('Scan the whole history again instead of only the new messages.')} checked={force} onCheckedChange={setForce} />
            <Switch
              label={t('One type at a time')}
              description={t('Slower, but Telegram is less likely to limit the requests. The default is in Settings > Channels.')}
              checked={oneTypeAtATime}
              onCheckedChange={setOneByOne}
            />
            {result && (
              <div className="flex items-start gap-2 rounded-xl border border-border bg-surface-2/50 p-3 text-sm">
                {result.failed ? <TriangleAlert className="mt-0.5 size-4 shrink-0 text-warning" /> : <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-success" />}
                <div className="min-w-0">
                  <p className="font-medium text-fg">
                    {result.failed ? t('The last refresh failed') : result.canceled ? t('The last refresh was canceled') : t('Last refresh')}
                    <span className="ml-1.5 font-normal text-fg-muted">{formatRelative(result.finishedAt, getLanguage())}</span>
                  </p>
                  <p className="text-xs text-fg-muted">
                    {t('{total} new files: {audios} audio, {videos} video, {photos} photos, {documents} documents', {
                      total: result.total,
                      audios: result.audios,
                      videos: result.videos,
                      photos: result.photos,
                      documents: result.documents
                    })}
                  </p>
                  {result.failed && result.error && <p className="mt-0.5 break-words font-mono text-xs text-warning">{result.error}</p>}
                </div>
              </div>
            )}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
