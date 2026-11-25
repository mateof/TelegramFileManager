import {
  Button,
  cn,
  Dialog,
  DialogContent,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
  Field,
  IconButton,
  Input,
  Slider,
  Spinner,
  toast
} from '@tfm/ui';
import {
  Blend,
  ListPlus,
  Moon,
  MoreHorizontal,
  Pause,
  Play,
  Repeat,
  Repeat1,
  Save,
  Shuffle,
  SkipBack,
  SkipForward,
  Trash2,
  Volume1,
  Volume2,
  VolumeX
} from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { audioToCandidate, candidateToWrite } from '@/features/playlists/convert';
import { openAddToPlaylist } from '@/features/playlists/public';
import { invalidatePlaylists } from '@/features/playlists/queries';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { getEngine } from '../engine';
import { formatTime } from '../format';
import { selectCurrent, usePlayer } from '../store';

const engine = () => getEngine();

export function PlayPauseButton({ size = 'md', className }: { size?: 'md' | 'lg' | 'xl'; className?: string }) {
  const status = usePlayer((s) => s.status);
  const playing = status === 'playing' || status === 'buffering';
  const loading = status === 'loading' || status === 'buffering';
  const dims = { md: 'size-10 [&_svg]:size-5', lg: 'size-12 [&_svg]:size-6', xl: 'size-16 [&_svg]:size-7' }[size];
  return (
    <button
      type="button"
      onClick={() => engine().toggle()}
      aria-label={playing ? t('Pause') : t('Play')}
      title={playing ? t('Pause') : t('Play')}
      className={cn(
        'relative flex shrink-0 items-center justify-center rounded-full bg-fg text-bg shadow-elevated transition-transform duration-150 hover:scale-105 active:scale-95',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
        dims,
        className
      )}
    >
      {playing ? <Pause className="fill-current" /> : <Play className="translate-x-px fill-current" />}
      {loading && <Spinner size="sm" className="absolute -right-0.5 -top-0.5 text-primary" />}
    </button>
  );
}

export function ShuffleButton({ size = 'sm' }: { size?: 'sm' | 'md' }) {
  const shuffle = usePlayer((s) => s.shuffle);
  return (
    <IconButton
      label={shuffle ? t('Shuffle on') : t('Shuffle off')}
      size={size}
      aria-pressed={shuffle}
      onClick={() => engine().toggleShuffle()}
      className={cn(shuffle && 'text-accent-400 hover:text-accent-300')}
    >
      <Shuffle />
    </IconButton>
  );
}

export function RepeatButton({ size = 'sm' }: { size?: 'sm' | 'md' }) {
  const repeat = usePlayer((s) => s.repeat);
  const label = repeat === 'off' ? t('Repeat off') : repeat === 'all' ? t('Repeat all') : t('Repeat one');
  return (
    <IconButton label={label} size={size} aria-pressed={repeat !== 'off'} onClick={() => engine().cycleRepeat()} className={cn(repeat !== 'off' && 'text-accent-400 hover:text-accent-300')}>
      {repeat === 'one' ? <Repeat1 /> : <Repeat />}
    </IconButton>
  );
}

/** Shuffle · previous · play/pause · next · repeat. */
export function Transport({ size = 'md', modes = true }: { size?: 'md' | 'lg'; modes?: boolean }) {
  const btn = size === 'lg' ? 'md' : 'sm';
  return (
    <div className={cn('flex items-center justify-center', size === 'lg' ? 'gap-4' : 'gap-2')}>
      {modes && <ShuffleButton size={btn} />}
      <IconButton label={t('Previous')} size={size === 'lg' ? 'lg' : 'md'} onClick={() => void engine().previous()} className="text-fg">
        <SkipBack className="fill-current" />
      </IconButton>
      <PlayPauseButton size={size === 'lg' ? 'xl' : 'md'} />
      <IconButton label={t('Next')} size={size === 'lg' ? 'lg' : 'md'} onClick={() => void engine().next()} className="text-fg">
        <SkipForward className="fill-current" />
      </IconButton>
      {modes && <RepeatButton size={btn} />}
    </div>
  );
}

export function VolumeControl({ className }: { className?: string }) {
  const volume = usePlayer((s) => s.volume);
  const muted = usePlayer((s) => s.muted);
  const shown = muted ? 0 : volume;
  const Icon = shown === 0 ? VolumeX : shown < 0.5 ? Volume1 : Volume2;
  return (
    <div className={cn('flex items-center gap-1', className)}>
      <IconButton label={muted ? t('Unmute') : t('Mute')} size="sm" onClick={() => engine().toggleMute()}>
        <Icon />
      </IconButton>
      <Slider
        className="w-24"
        min={0}
        max={100}
        step={1}
        value={[Math.round(shown * 100)]}
        onValueChange={([v]) => engine().setVolume(v / 100)}
        thumbLabels={[t('Volume')]}
      />
    </div>
  );
}

function useNow(active: boolean) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) return;
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [active]);
  return now;
}

/** Remaining sleep time ("12:30"), or null. */
function useSleepRemaining(): string | null {
  const endsAt = usePlayer((s) => s.sleepEndsAt);
  const atEnd = usePlayer((s) => s.sleepAtEndOfTrack);
  const now = useNow(!!endsAt);
  if (atEnd) return t('End of track');
  if (!endsAt) return null;
  return formatTime(Math.max(0, (endsAt - now) / 1000));
}

const SLEEP_MINUTES = [5, 15, 30, 45, 60, 90];

export function SleepTimerMenu({ size = 'sm' }: { size?: 'sm' | 'md' }) {
  const remaining = useSleepRemaining();
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size={size} aria-label={t('Sleep timer')} title={t('Sleep timer')} className={cn('px-2', remaining && 'text-accent-400')}>
          <Moon />
          {remaining && <span className="text-xs tabular-nums">{remaining}</span>}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" side="top">
        <DropdownMenuLabel>{t('Sleep timer')}</DropdownMenuLabel>
        {SLEEP_MINUTES.map((m) => (
          <DropdownMenuItem key={m} onSelect={() => engine().setSleepTimer(m)}>
            {t('{count} minutes', { count: m })}
          </DropdownMenuItem>
        ))}
        <DropdownMenuItem onSelect={() => engine().setSleepTimer('track')}>{t('End of track')}</DropdownMenuItem>
        {remaining && (
          <>
            <DropdownMenuSeparator />
            <DropdownMenuItem destructive onSelect={() => engine().cancelSleepTimer()}>
              {t('Turn off the timer')}
            </DropdownMenuItem>
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

const CROSSFADES = [0, 2, 4, 6, 8, 12];

/** Save the queue · add to playlist · crossfade · clear. */
export function PlayerMoreMenu({ side = 'top' }: { side?: 'top' | 'bottom' }) {
  const current = usePlayer(selectCurrent);
  const crossfade = usePlayer((s) => s.crossfadeSeconds);
  const [saveOpen, setSaveOpen] = useState(false);
  const candidate = current ? audioToCandidate(current) : null;
  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <IconButton label={t('More options')} size="sm">
            <MoreHorizontal />
          </IconButton>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" side={side}>
          <DropdownMenuItem icon={<ListPlus />} disabled={!candidate} onSelect={() => candidate && openAddToPlaylist([candidate])}>
            {t('Add to playlist')}
          </DropdownMenuItem>
          <DropdownMenuItem icon={<Save />} onSelect={() => setSaveOpen(true)}>
            {t('Save queue as playlist')}
          </DropdownMenuItem>
          <DropdownMenuSub>
            <DropdownMenuSubTrigger icon={<Blend />}>{t('Crossfade')}</DropdownMenuSubTrigger>
            <DropdownMenuSubContent>
              <DropdownMenuRadioGroup value={String(crossfade)} onValueChange={(v) => usePlayer.getState().setCrossfade(Number(v))}>
                {CROSSFADES.map((s) => (
                  <DropdownMenuRadioItem key={s} value={String(s)}>
                    {s === 0 ? t('Off (gapless)') : t('{count} seconds', { count: s })}
                  </DropdownMenuRadioItem>
                ))}
              </DropdownMenuRadioGroup>
            </DropdownMenuSubContent>
          </DropdownMenuSub>
          <DropdownMenuSeparator />
          <DropdownMenuItem icon={<Trash2 />} destructive onSelect={() => engine().clearQueue()}>
            {t('Clear queue and close')}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
      {saveOpen && <SaveQueueDialog onClose={() => setSaveOpen(false)} />}
    </>
  );
}

function SaveQueueDialog({ onClose }: { onClose: () => void }) {
  const queue = usePlayer((s) => s.queue);
  const [name, setName] = useState('');
  const [saving, setSaving] = useState(false);
  const candidates = queue.map(audioToCandidate).filter((c) => c !== null);
  const skipped = queue.length - candidates.length;

  const save = async (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim() || candidates.length === 0) return;
    setSaving(true);
    try {
      const p = await api.playlists.create({ name: name.trim(), tracks: candidates.map(candidateToWrite) });
      toast.success(t('Playlist "{name}" created with {count} tracks', { name: p.name, count: candidates.length }));
      void invalidatePlaylists();
      onClose();
    } catch (err) {
      toast.error(t('Could not create the playlist'), { description: errorMessage(err) });
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        size="sm"
        title={t('Save queue as playlist')}
        description={t('{count} tracks', { count: candidates.length })}
        footer={
          <>
            <Button variant="ghost" onClick={onClose}>
              {t('Cancel')}
            </Button>
            <Button type="submit" form="tfm-save-queue" loading={saving} disabled={!name.trim() || candidates.length === 0}>
              {t('Save')}
            </Button>
          </>
        }
      >
        <form id="tfm-save-queue" onSubmit={save} className="flex flex-col gap-3">
          <Field label={t('Name')}>
            {({ id }) => <Input id={id} autoFocus value={name} onChange={(e) => setName(e.target.value)} placeholder={t('My mix')} />}
          </Field>
          {skipped > 0 && <p className="text-xs text-warning">{t('{count} tracks cannot be stored in a playlist and will be left out.', { count: skipped })}</p>}
        </form>
      </DialogContent>
    </Dialog>
  );
}
