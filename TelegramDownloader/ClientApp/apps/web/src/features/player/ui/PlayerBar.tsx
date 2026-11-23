import { cn, IconButton, Sheet, SheetContent } from '@tfm/ui';
import { ChevronUp, ListMusic, Maximize2, SkipForward } from 'lucide-react';
import { lazy, Suspense } from 'react';
import { t } from '@/i18n';
import '../player.css';
import { getEngine } from '../engine';
import { formatTime } from '../format';
import { usePlayer } from '../store';
import { Artwork } from './Artwork';
import { PlayerMoreMenu, PlayPauseButton, SleepTimerMenu, Transport, VolumeControl } from './Controls';
import { QueuePanel } from './QueuePanel';
import { SeekBar } from './SeekBar';
import { useNowPlaying, useSyncMediaSession } from './useNowPlaying';
import { usePlayerShortcuts } from './usePlayerShortcuts';

const ExpandedPlayer = lazy(() => import('./ExpandedPlayer'));

function Times() {
  const position = usePlayer((s) => s.position);
  const duration = usePlayer((s) => s.duration);
  const buffered = usePlayer((s) => s.buffered);
  return (
    <div className="flex w-full items-center gap-2.5 text-[11px] tabular-nums text-fg-muted">
      <span className="w-10 text-right">{formatTime(position)}</span>
      <SeekBar className="flex-1" position={position} duration={duration} buffered={buffered} onSeek={(s) => getEngine().seek(s)} label={t('Seek')} />
      <span className="w-10">{formatTime(duration)}</span>
    </div>
  );
}

function MiniProgress() {
  const position = usePlayer((s) => s.position);
  const duration = usePlayer((s) => s.duration);
  const pct = duration > 0 ? Math.min(100, (position / duration) * 100) : 0;
  return (
    <div aria-hidden className="absolute inset-x-3 bottom-0 h-0.5 overflow-hidden rounded-full bg-fg/10">
      <div className="h-full bg-primary" style={{ width: `${pct}%` }} />
    </div>
  );
}

/**
 * The always-visible player: a full-width bar on desktop (music-app style) and
 * a compact card above the bottom navigation on phones. Opens the expanded
 * view (lazy) and the queue.
 */
export default function PlayerBar() {
  usePlayerShortcuts();
  useSyncMediaSession();
  const { track, title, subtitle, cover } = useNowPlaying();
  const expanded = usePlayer((s) => s.expanded);
  const queueOpen = usePlayer((s) => s.queueOpen);
  const status = usePlayer((s) => s.status);
  const error = usePlayer((s) => s.error);
  const { setExpanded, setQueueOpen } = usePlayer.getState();
  if (!track) return null;

  const sub = status === 'error' && error ? <span className="text-danger">{error}</span> : subtitle;
  const expand = () => setExpanded(true);

  return (
    <>
      <section aria-label={t('Player')} className="relative z-20 shrink-0">
        {/* Desktop / tablet */}
        <div className="glass hidden h-20 grid-cols-[minmax(0,1fr)_minmax(0,2fr)_minmax(0,1fr)] items-center gap-4 border-t border-border px-4 md:grid">
          <div className="flex min-w-0 items-center gap-3">
            <button type="button" onClick={expand} aria-label={t('Open the player')} className="group relative rounded-xl focus-visible:outline-2 focus-visible:outline-ring">
              <Artwork src={cover} seed={title} className="size-14 rounded-xl" />
              <span className="absolute inset-0 flex items-center justify-center rounded-xl bg-black/40 text-white opacity-0 transition-opacity group-hover:opacity-100">
                <ChevronUp className="size-5" />
              </span>
            </button>
            <div className="min-w-0">
              <button type="button" onClick={expand} className="block max-w-full truncate text-left text-sm font-semibold text-fg hover:underline">
                {title}
              </button>
              <p className="truncate text-xs text-fg-muted">{sub}</p>
            </div>
          </div>
          <div className="flex min-w-0 flex-col items-center gap-1">
            <Transport />
            <Times />
          </div>
          <div className="flex min-w-0 items-center justify-end gap-1">
            <SleepTimerMenu />
            <IconButton label={t('Queue')} size="sm" aria-pressed={queueOpen} onClick={() => setQueueOpen(!queueOpen)} className={cn(queueOpen && 'text-accent-400')}>
              <ListMusic />
            </IconButton>
            <VolumeControl className="hidden lg:flex" />
            <PlayerMoreMenu />
            <IconButton label={t('Expand')} size="sm" onClick={expand}>
              <Maximize2 />
            </IconButton>
          </div>
        </div>

        {/* Phone */}
        <div className="px-2 pb-1.5 pt-1 md:hidden">
          <div className="glass-strong relative flex items-center gap-3 rounded-2xl border border-border p-2 shadow-elevated">
            <button type="button" onClick={expand} className="flex min-w-0 flex-1 items-center gap-3 text-left" aria-label={t('Open the player')}>
              <Artwork src={cover} seed={title} className="size-11 rounded-xl" />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-semibold text-fg">{title}</span>
                <span className="block truncate text-xs text-fg-muted">{sub}</span>
              </span>
            </button>
            <PlayPauseButton />
            <IconButton label={t('Next')} onClick={() => void getEngine().next()}>
              <SkipForward className="fill-current" />
            </IconButton>
            <MiniProgress />
          </div>
        </div>
      </section>

      <Sheet open={queueOpen && !expanded} onOpenChange={setQueueOpen}>
        <SheetContent side="right" title={t('Queue')} className="w-[min(28rem,94vw)]">
          <QueuePanel className="h-full" />
        </SheetContent>
      </Sheet>

      {expanded && (
        <Suspense fallback={null}>
          <ExpandedPlayer />
        </Suspense>
      )}
    </>
  );
}
