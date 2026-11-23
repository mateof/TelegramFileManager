import { cn, IconButton } from '@tfm/ui';
import { ChevronDown, ListMusic, ListPlus } from 'lucide-react';
import { Dialog as DialogPrimitive } from 'radix-ui';
import { audioToCandidate } from '@/features/playlists/convert';
import { openAddToPlaylist } from '@/features/playlists/public';
import { t } from '@/i18n';
import { getEngine } from '../engine';
import { formatTime } from '../format';
import { fallbackGradient } from '../metadata';
import { usePlayer } from '../store';
import { Artwork } from './Artwork';
import { PlayerMoreMenu, SleepTimerMenu, Transport, VolumeControl } from './Controls';
import { QueuePanel } from './QueuePanel';
import { SeekBar } from './SeekBar';
import { useNowPlaying } from './useNowPlaying';

function BigSeek() {
  const position = usePlayer((s) => s.position);
  const duration = usePlayer((s) => s.duration);
  const buffered = usePlayer((s) => s.buffered);
  return (
    <div className="flex flex-col gap-1.5">
      <SeekBar size="md" position={position} duration={duration} buffered={buffered} onSeek={(s) => getEngine().seek(s)} label={t('Seek')} />
      <div className="flex justify-between text-xs tabular-nums text-fg-muted">
        <span>{formatTime(position)}</span>
        <span>-{formatTime(Math.max(0, duration - position))}</span>
      </div>
    </div>
  );
}

/** Full-screen "now playing": large artwork over a blurred ambient copy of it, controls and the queue. */
export default function ExpandedPlayer() {
  const { track, title, subtitle, album, cover } = useNowPlaying();
  const queueOpen = usePlayer((s) => s.queueOpen);
  const { setExpanded, setQueueOpen } = usePlayer.getState();
  if (!track) return null;
  const candidate = audioToCandidate(track);

  return (
    <DialogPrimitive.Root open onOpenChange={(o) => !o && setExpanded(false)}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Content
          data-player-keys
          aria-describedby={undefined}
          className="fixed inset-0 z-50 flex flex-col overflow-hidden bg-bg text-fg outline-none data-[state=open]:animate-slide-in-bottom"
        >
          <DialogPrimitive.Title className="sr-only">{t('Now playing')}</DialogPrimitive.Title>
          {/* Ambient background */}
          <div aria-hidden className="pointer-events-none absolute inset-0">
            {cover ? (
              <img src={cover} alt="" className="tfm-player-ambient size-full object-cover opacity-60 blur-3xl saturate-150" />
            ) : (
              <div className="tfm-player-ambient size-full opacity-50 blur-3xl" style={{ backgroundImage: fallbackGradient(title) }} />
            )}
            <div className="absolute inset-0 bg-gradient-to-b from-bg/40 via-bg/65 to-bg/90" />
          </div>

          <header className="relative flex items-center gap-2 px-3 pb-2 pt-[max(0.75rem,env(safe-area-inset-top))] sm:px-6">
            <IconButton label={t('Minimize the player')} onClick={() => setExpanded(false)}>
              <ChevronDown className="size-6!" />
            </IconButton>
            <div className="min-w-0 flex-1 text-center">
              <p className="text-[11px] font-semibold uppercase tracking-widest text-fg-subtle">{t('Now playing')}</p>
              {track.source && <p className="truncate text-xs text-fg-muted">{track.source}</p>}
            </div>
            <IconButton label={t('Queue')} aria-pressed={queueOpen} onClick={() => setQueueOpen(!queueOpen)} className={cn('lg:hidden', queueOpen && 'text-accent-400')}>
              <ListMusic />
            </IconButton>
            <PlayerMoreMenu side="bottom" />
          </header>

          <div className="relative flex min-h-0 flex-1 gap-8 px-5 pb-[max(1.25rem,env(safe-area-inset-bottom))] sm:px-8 lg:px-12">
            <div className={cn('mx-auto flex min-h-0 w-full max-w-xl flex-1 flex-col justify-center gap-6 lg:max-w-2xl', queueOpen && 'max-lg:hidden')}>
              <div className="flex min-h-0 flex-1 items-center justify-center py-2">
                <Artwork
                  src={cover}
                  seed={title}
                  className="aspect-square max-h-full w-full max-w-[min(28rem,100%)] rounded-3xl shadow-[0_30px_80px_-20px_rgb(0_0_0/0.6)]"
                  iconClassName="size-1/4"
                />
              </div>
              <div className="flex items-end gap-3">
                <div className="min-w-0 flex-1">
                  <h2 className="truncate text-2xl font-bold tracking-tight text-fg sm:text-3xl">{title}</h2>
                  <p className="truncate text-base text-fg-muted">{[subtitle, album].filter(Boolean).join(' · ')}</p>
                </div>
                <IconButton label={t('Add to playlist')} disabled={!candidate} onClick={() => candidate && openAddToPlaylist([candidate])}>
                  <ListPlus />
                </IconButton>
              </div>
              <BigSeek />
              <Transport size="lg" />
              <div className="flex items-center justify-between gap-2">
                <VolumeControl className="max-sm:hidden" />
                <SleepTimerMenu size="md" />
                <IconButton label={t('Queue')} aria-pressed={queueOpen} onClick={() => setQueueOpen(!queueOpen)} className={cn('max-lg:hidden', queueOpen && 'text-accent-400')}>
                  <ListMusic />
                </IconButton>
              </div>
            </div>

            {queueOpen && (
              <aside className="glass flex min-h-0 w-full flex-col rounded-3xl border border-border pt-3 shadow-elevated lg:w-[26rem] lg:shrink-0">
                <h2 className="px-4 text-base font-semibold">{t('Queue')}</h2>
                <QueuePanel className="min-h-0 flex-1" />
              </aside>
            )}
          </div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
