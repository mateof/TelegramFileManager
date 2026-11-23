import { cn, Slider, Spinner } from '@tfm/ui';
import { AlertTriangle, Maximize, Minimize, Pause, PictureInPicture2, Play, RotateCcw, RotateCw, Volume2, VolumeX } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { t } from '@/i18n';
import { api } from '@/lib/api';
import { formatTime } from '../format';
import type { ViewerItem } from '../public';
import { usePlayer, type TimeRange } from '../store';
import { SeekBar } from '../ui/SeekBar';
import { fallbackVideoSource, pickVideoSource, WATCH_REPORT_MS, watchReport } from './videoSource';

const SPEEDS = [1, 1.25, 1.5, 2, 0.5, 0.75];
const HIDE_AFTER_MS = 2500;

function ranges(el: HTMLVideoElement): TimeRange[] {
  const out: TimeRange[] = [];
  for (let i = 0; i < el.buffered.length; i++) out.push({ start: el.buffered.start(i), end: el.buffered.end(i) });
  return out;
}

function ControlButton({ label, onClick, children, className }: { label: string; onClick: () => void; children: ReactNode; className?: string }) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      className={cn('flex size-9 items-center justify-center rounded-full text-white transition-colors hover:bg-white/15 focus-visible:outline-2 focus-visible:outline-white [&_svg]:size-5', className)}
    >
      {children}
    </button>
  );
}

/** Native <video> with custom glass controls, transcoding fallback and library watch progress. */
export default function VideoView({ item }: { item: ViewerItem }) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const boxRef = useRef<HTMLDivElement>(null);
  const [src, setSrc] = useState(() => pickVideoSource(item, (m) => document.createElement('video').canPlayType(m)));
  const [playing, setPlaying] = useState(false);
  const [waiting, setWaiting] = useState(true);
  const [position, setPosition] = useState(0);
  const [duration, setDuration] = useState(0);
  const [buffered, setBuffered] = useState<TimeRange[]>([]);
  const [volume, setVolume] = useState(1);
  const [muted, setMuted] = useState(false);
  const [speed, setSpeed] = useState(1);
  const [fullscreen, setFullscreen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [controls, setControls] = useState(true);
  const hideTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const resumeAt = useRef<number>(item.watch?.startAtSeconds ?? 0);
  const lastReport = useRef(0);

  // ---- watch progress (media library)
  const report = useCallback(
    (ended = false) => {
      const el = videoRef.current;
      if (!item.watch || !el) return;
      const body = watchReport(el.currentTime, el.duration, ended);
      if (!body) return;
      lastReport.current = Date.now();
      void api.library.updateWatch(item.watch.channelId, item.watch.fileId, body).catch(() => undefined);
    },
    [item.watch]
  );
  useEffect(() => () => report(), [report]);

  // ---- the global audio player pauses while a video plays
  const pauseMusic = () => {
    const status = usePlayer.getState().status;
    if (status === 'playing' || status === 'buffering') void import('../engine').then((m) => m.getEngine().pause());
  };

  // ---- auto-hide the controls while playing
  const poke = useCallback(() => {
    setControls(true);
    if (hideTimer.current) clearTimeout(hideTimer.current);
    hideTimer.current = setTimeout(() => {
      if (videoRef.current && !videoRef.current.paused) setControls(false);
    }, HIDE_AFTER_MS);
  }, []);
  useEffect(() => () => {
    if (hideTimer.current) clearTimeout(hideTimer.current);
  }, []);

  const toggle = useCallback(() => {
    const el = videoRef.current;
    if (!el) return;
    if (el.paused) void el.play().catch(() => undefined);
    else el.pause();
  }, []);
  const seek = useCallback((s: number) => {
    const el = videoRef.current;
    if (el && Number.isFinite(el.duration)) el.currentTime = Math.max(0, Math.min(el.duration, s));
  }, []);
  const toggleFullscreen = useCallback(() => {
    if (document.fullscreenElement) void document.exitFullscreen().catch(() => undefined);
    else void boxRef.current?.requestFullscreen?.().catch(() => undefined);
  }, []);

  useEffect(() => {
    const onFs = () => setFullscreen(!!document.fullscreenElement);
    document.addEventListener('fullscreenchange', onFs);
    return () => document.removeEventListener('fullscreenchange', onFs);
  }, []);

  // ---- keyboard
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
      if (e.target instanceof Element && e.target.closest('input, textarea, [role="slider"], button')) {
        if (e.key !== 'f' && e.key !== 'F') return;
      }
      const el = videoRef.current;
      if (!el) return;
      switch (e.key) {
        case ' ':
        case 'k':
        case 'K':
          toggle();
          break;
        case 'ArrowLeft':
          seek(el.currentTime - (e.shiftKey ? 30 : 10));
          break;
        case 'ArrowRight':
          seek(el.currentTime + (e.shiftKey ? 30 : 10));
          break;
        case 'ArrowUp':
          el.volume = Math.min(1, el.volume + 0.05);
          break;
        case 'ArrowDown':
          el.volume = Math.max(0, el.volume - 0.05);
          break;
        case 'm':
        case 'M':
          el.muted = !el.muted;
          break;
        case 'f':
        case 'F':
          toggleFullscreen();
          break;
        default:
          return;
      }
      e.preventDefault();
      poke();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [toggle, seek, toggleFullscreen, poke]);

  const onError = () => {
    const el = videoRef.current;
    const next = fallbackVideoSource(item, src);
    if (next) {
      resumeAt.current = el?.currentTime || resumeAt.current;
      setSrc(next);
      setWaiting(true);
      return;
    }
    setError(t('This video cannot be played in the browser.'));
    setWaiting(false);
  };

  const cycleSpeed = () => {
    const next = SPEEDS[(SPEEDS.indexOf(speed) + 1) % SPEEDS.length];
    if (videoRef.current) videoRef.current.playbackRate = next;
    setSpeed(next);
  };

  const pip = () => {
    const el = videoRef.current;
    if (!el) return;
    if (document.pictureInPictureElement) void document.exitPictureInPicture().catch(() => undefined);
    else void el.requestPictureInPicture?.().catch(() => undefined);
  };

  const showControls = controls || !playing;

  return (
    <div
      ref={boxRef}
      className={cn('relative flex size-full items-center justify-center bg-black', !showControls && 'cursor-none')}
      onPointerMove={poke}
      onPointerDown={poke}
    >
      <video
        ref={videoRef}
        src={src}
        autoPlay
        playsInline
        preload="auto"
        className="max-h-full max-w-full"
        onClick={toggle}
        onDoubleClick={toggleFullscreen}
        onLoadedMetadata={(e) => {
          const el = e.currentTarget;
          setDuration(el.duration);
          if (resumeAt.current > 0 && resumeAt.current < el.duration - 5) el.currentTime = resumeAt.current;
          resumeAt.current = 0;
        }}
        onDurationChange={(e) => setDuration(e.currentTarget.duration)}
        onTimeUpdate={(e) => {
          const el = e.currentTarget;
          setPosition(el.currentTime);
          if (item.watch && !el.paused && Date.now() - lastReport.current >= WATCH_REPORT_MS) report();
        }}
        onProgress={(e) => setBuffered(ranges(e.currentTarget))}
        onPlay={() => {
          setPlaying(true);
          pauseMusic();
          poke();
        }}
        onPause={() => {
          setPlaying(false);
          setControls(true);
          report();
        }}
        onEnded={() => report(true)}
        onWaiting={() => setWaiting(true)}
        onPlaying={() => setWaiting(false)}
        onCanPlay={() => setWaiting(false)}
        onVolumeChange={(e) => {
          setVolume(e.currentTarget.volume);
          setMuted(e.currentTarget.muted);
        }}
        onError={onError}
      />

      {waiting && !error && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
          <Spinner size="lg" className="text-white" />
        </div>
      )}
      {error && (
        <div className="absolute inset-0 flex flex-col items-center justify-center gap-3 p-6 text-center">
          <AlertTriangle className="size-10 text-warning" />
          <p className="max-w-sm text-sm text-white/80">{error}</p>
        </div>
      )}

      <div
        className={cn(
          'absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/80 via-black/40 to-transparent px-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] pt-12 transition-opacity duration-300 sm:px-6',
          showControls ? 'opacity-100' : 'pointer-events-none opacity-0'
        )}
      >
        <SeekBar onDark size="md" position={position} duration={duration} buffered={buffered} onSeek={seek} label={t('Seek')} />
        <div className="mt-1.5 flex items-center gap-1 text-white">
          <ControlButton label={playing ? t('Pause') : t('Play')} onClick={toggle}>
            {playing ? <Pause className="fill-current" /> : <Play className="fill-current" />}
          </ControlButton>
          <ControlButton label={t('Back 10 seconds')} onClick={() => seek(position - 10)} className="max-sm:hidden">
            <RotateCcw />
          </ControlButton>
          <ControlButton label={t('Forward 10 seconds')} onClick={() => seek(position + 10)} className="max-sm:hidden">
            <RotateCw />
          </ControlButton>
          <span className="ml-1 text-xs tabular-nums text-white/80">
            {formatTime(position)} / {formatTime(duration)}
          </span>
          <div className="flex-1" />
          <ControlButton label={muted ? t('Unmute') : t('Mute')} onClick={() => videoRef.current && (videoRef.current.muted = !videoRef.current.muted)}>
            {muted || volume === 0 ? <VolumeX /> : <Volume2 />}
          </ControlButton>
          <Slider
            className="w-20 max-sm:hidden"
            min={0}
            max={100}
            value={[Math.round((muted ? 0 : volume) * 100)]}
            onValueChange={([v]) => {
              const el = videoRef.current;
              if (!el) return;
              el.volume = v / 100;
              el.muted = v === 0;
            }}
            thumbLabels={[t('Volume')]}
          />
          <button
            type="button"
            onClick={cycleSpeed}
            aria-label={t('Playback speed')}
            title={t('Playback speed')}
            className="h-8 min-w-11 rounded-full px-2 text-xs font-semibold tabular-nums text-white hover:bg-white/15 focus-visible:outline-2 focus-visible:outline-white"
          >
            {speed}×
          </button>
          {'pictureInPictureEnabled' in document && (
            <ControlButton label={t('Picture in picture')} onClick={pip} className="max-sm:hidden">
              <PictureInPicture2 />
            </ControlButton>
          )}
          <ControlButton label={fullscreen ? t('Exit full screen') : t('Full screen')} onClick={toggleFullscreen}>
            {fullscreen ? <Minimize /> : <Maximize />}
          </ControlButton>
        </div>
      </div>
    </div>
  );
}
