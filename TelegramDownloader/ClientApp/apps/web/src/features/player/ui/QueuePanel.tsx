import { cn, IconButton } from '@tfm/ui';
import { useVirtualizer } from '@tanstack/react-virtual';
import { GripVertical, X } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { t } from '@/i18n';
import { getEngine } from '../engine';
import { displayTitle } from '../queue';
import { usePlayer } from '../store';
import { Artwork } from './Artwork';
import { useDragReorder } from './useDragReorder';

const ROW = 56;

/** Animated bars next to the track that is playing. */
export function PlayingBars({ active }: { active: boolean }) {
  return (
    <span aria-hidden className="flex h-3.5 items-end gap-0.5">
      {[0, 1, 2].map((i) => (
        <span
          key={i}
          className={cn('w-0.5 rounded-full bg-accent-400', active ? 'animate-[tfm-eq_0.9s_ease-in-out_infinite]' : 'h-1')}
          style={active ? { animationDelay: `${i * 0.18}s`, height: '100%' } : undefined}
        />
      ))}
    </span>
  );
}

/** The play queue: virtualized, drag (or Alt+↑/↓) to reorder, click to play, × to remove. */
export function QueuePanel({ className }: { className?: string }) {
  const queue = usePlayer((s) => s.queue);
  const index = usePlayer((s) => s.index);
  const status = usePlayer((s) => s.status);
  const scrollRef = useRef<HTMLDivElement>(null);
  const engine = getEngine();

  const virtualizer = useVirtualizer({
    count: queue.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW,
    overscan: 8,
    getItemKey: (i) => queue[i]?.qid ?? i
  });

  const { drag, handleProps, onRowKeyDown, offset } = useDragReorder({
    count: queue.length,
    rowHeight: ROW,
    onMove: (from, to) => engine.move(from, to),
    getScrollElement: () => scrollRef.current
  });

  // Start with the current track in view
  useEffect(() => {
    if (index >= 0) virtualizer.scrollToIndex(index, { align: 'center' });
    // only when the panel opens
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const focusRow = (i: number) => {
    requestAnimationFrame(() => scrollRef.current?.querySelector<HTMLElement>(`[data-row="${i}"]`)?.focus());
  };

  return (
    <div className={cn('flex min-h-0 flex-col', className)}>
      <div className="flex items-center justify-between px-4 pb-2 pt-1 text-xs text-fg-muted">
        <span>{t('{count} tracks in the queue', { count: queue.length })}</span>
        <span className="hidden sm:inline">{t('Drag or Alt+↑/↓ to reorder')}</span>
      </div>
      <div ref={scrollRef} className="min-h-0 flex-1 overflow-y-auto px-2 pb-3" data-no-player-keys>
        <ul role="list" aria-label={t('Queue')} className="relative" style={{ height: virtualizer.getTotalSize() }}>
          {virtualizer.getVirtualItems().map((v) => {
            const track = queue[v.index];
            if (!track) return null;
            const isCurrent = v.index === index;
            const dragging = drag?.from === v.index;
            return (
              <li
                key={v.key}
                data-row={v.index}
                tabIndex={0}
                aria-current={isCurrent || undefined}
                onKeyDown={(e) => {
                  if (onRowKeyDown(v.index, e)) focusRow(e.key === 'ArrowUp' ? v.index - 1 : v.index + 1);
                  else if (e.key === 'Enter') void engine.playAt(v.index);
                  else if (e.key === 'Delete') engine.remove(track.qid);
                }}
                onDoubleClick={() => void engine.playAt(v.index)}
                className={cn(
                  'group absolute inset-x-0 flex items-center gap-2 rounded-xl pr-1 outline-none transition-[background-color,box-shadow]',
                  'hover:bg-surface-2/70 focus-visible:ring-2 focus-visible:ring-ring',
                  isCurrent && 'bg-primary-soft',
                  dragging ? 'z-10 bg-surface-2 shadow-elevated' : drag && 'transition-transform duration-150'
                )}
                style={{ height: ROW, transform: `translateY(${v.start + offset(v.index)}px)` }}
              >
                <span {...handleProps(v.index)} aria-hidden className="flex h-full w-6 shrink-0 items-center justify-center text-fg-subtle opacity-60 group-hover:opacity-100">
                  <GripVertical className="size-4" />
                </span>
                <button type="button" onClick={() => void engine.playAt(v.index)} className="flex min-w-0 flex-1 items-center gap-3 text-left" tabIndex={-1}>
                  <Artwork src={track.coverUrl} seed={track.title} className="size-10 rounded-lg" />
                  <span className="min-w-0 flex-1">
                    <span className={cn('flex items-center gap-2 truncate text-sm font-medium', isCurrent ? 'text-accent-300' : 'text-fg')}>
                      {isCurrent && <PlayingBars active={status === 'playing'} />}
                      <span className="truncate">{displayTitle(track)}</span>
                    </span>
                    <span className="block truncate text-xs text-fg-muted">{track.artist ?? track.source ?? ''}</span>
                  </span>
                </button>
                <IconButton label={t('Remove from the queue')} size="xs" className="opacity-0 group-hover:opacity-100 focus-visible:opacity-100 max-md:opacity-70" onClick={() => engine.remove(track.qid)}>
                  <X />
                </IconButton>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}
