import { cn } from '@tfm/ui';
import { useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import { formatTime } from '../format';
import type { TimeRange } from '../store';

export interface SeekBarProps {
  position: number;
  duration: number;
  buffered?: TimeRange[];
  onSeek: (seconds: number) => void;
  label: string;
  className?: string;
  /** Thicker track (expanded player / video). */
  size?: 'sm' | 'md';
  /** Always on a dark backdrop (video player). */
  onDark?: boolean;
}

/**
 * Seek bar with buffered ranges, hover time and scrubbing (mouse/touch), and
 * the slider keyboard model (←/→ 5 s, PageUp/PageDown 30 s, Home/End).
 */
export function SeekBar({ position, duration, buffered = [], onSeek, label, className, size = 'sm', onDark }: SeekBarProps) {
  const ref = useRef<HTMLDivElement>(null);
  const [scrub, setScrub] = useState<number | null>(null);
  const [hover, setHover] = useState<{ x: number; time: number } | null>(null);
  const valid = Number.isFinite(duration) && duration > 0;
  const shown = scrub ?? position;
  const pct = valid ? Math.min(100, (shown / duration) * 100) : 0;

  const timeAt = (clientX: number) => {
    const rect = ref.current?.getBoundingClientRect();
    if (!rect || !valid) return 0;
    const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    return ratio * duration;
  };

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    if (!valid || e.button !== 0) return;
    e.currentTarget.setPointerCapture?.(e.pointerId);
    setScrub(timeAt(e.clientX));
  };
  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    if (!valid) return;
    const rect = ref.current?.getBoundingClientRect();
    if (rect) setHover({ x: e.clientX - rect.left, time: timeAt(e.clientX) });
    if (scrub !== null) setScrub(timeAt(e.clientX));
  };
  const onPointerUp = (e: PointerEvent<HTMLDivElement>) => {
    if (scrub === null) return;
    onSeek(timeAt(e.clientX));
    setScrub(null);
  };
  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (!valid) return;
    const step = { ArrowLeft: -5, ArrowRight: 5, ArrowDown: -5, ArrowUp: 5, PageDown: -30, PageUp: 30 }[e.key];
    if (step !== undefined) onSeek(Math.max(0, Math.min(duration, position + step)));
    else if (e.key === 'Home') onSeek(0);
    else if (e.key === 'End') onSeek(Math.max(0, duration - 1));
    else return;
    e.preventDefault();
    e.stopPropagation();
  };

  return (
    <div
      ref={ref}
      role="slider"
      tabIndex={valid ? 0 : -1}
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={valid ? Math.round(duration) : 0}
      aria-valuenow={Math.round(shown)}
      aria-valuetext={`${formatTime(shown)} / ${formatTime(duration)}`}
      aria-disabled={!valid || undefined}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={() => setScrub(null)}
      onPointerLeave={() => setHover(null)}
      onKeyDown={onKeyDown}
      className={cn(
        'group/seek relative flex h-4 cursor-pointer touch-none select-none items-center rounded-full outline-none',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
        !valid && 'cursor-default opacity-60',
        className
      )}
    >
      <div className={cn('relative w-full overflow-hidden rounded-full transition-[height] duration-150', onDark ? 'bg-white/20' : 'bg-fg/15', size === 'md' ? 'h-1.5 group-hover/seek:h-2' : 'h-1 group-hover/seek:h-1.5')}>
        {valid &&
          buffered.map((r, i) => (
            <div
              key={i}
              className={cn('absolute inset-y-0', onDark ? 'bg-white/30' : 'bg-fg/20')}
              style={{ left: `${(r.start / duration) * 100}%`, width: `${((r.end - r.start) / duration) * 100}%` }}
            />
          ))}
        <div className="absolute inset-y-0 left-0 rounded-full bg-primary" style={{ width: `${pct}%` }} />
      </div>
      <div
        aria-hidden
        className={cn(
          'pointer-events-none absolute top-1/2 size-3 -translate-x-1/2 -translate-y-1/2 rounded-full bg-white shadow-soft transition-opacity',
          scrub !== null ? 'opacity-100' : 'opacity-0 group-hover/seek:opacity-100 group-focus-visible/seek:opacity-100'
        )}
        style={{ left: `${pct}%` }}
      />
      {hover && valid && (
        <div
          aria-hidden
          className="pointer-events-none absolute -top-7 -translate-x-1/2 rounded-md bg-black/75 px-1.5 py-0.5 text-[11px] font-medium tabular-nums text-white"
          style={{ left: hover.x }}
        >
          {formatTime(scrub ?? hover.time)}
        </div>
      )}
    </div>
  );
}
