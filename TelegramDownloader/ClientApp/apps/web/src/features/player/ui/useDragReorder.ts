import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';

/**
 * Drag & drop reordering for lists with a fixed row height (virtualized or
 * not). Works with mouse, touch and pen (pointer events on a drag handle) and
 * from the keyboard (Alt+↑ / Alt+↓ on a row). Used by the queue panel and the
 * playlist page.
 */
export interface DragReorderOptions {
  count: number;
  rowHeight: number;
  onMove: (from: number, to: number) => void;
  /** Scroll container, auto-scrolled when dragging near its edges. */
  getScrollElement?: () => HTMLElement | null;
}

export interface DragState {
  from: number;
  to: number;
}

/** Visual offset (px) of row `index` while `drag` is in progress. */
export function dragOffset(index: number, drag: DragState | null, rowHeight: number): number {
  if (!drag || drag.from === drag.to) return 0;
  const { from, to } = drag;
  if (index === from) return (to - from) * rowHeight;
  if (from < to && index > from && index <= to) return -rowHeight;
  if (from > to && index >= to && index < from) return rowHeight;
  return 0;
}

export function useDragReorder({ count, rowHeight, onMove, getScrollElement }: DragReorderOptions) {
  const [drag, setDrag] = useState<DragState | null>(null);
  const session = useRef<{ from: number; startY: number; startScroll: number; lastY: number; to: number } | null>(null);
  const scrollTimer = useRef<ReturnType<typeof setInterval> | null>(null);
  const latest = useRef({ count, rowHeight, onMove, getScrollElement });
  useEffect(() => {
    latest.current = { count, rowHeight, onMove, getScrollElement };
  });

  const stopAutoScroll = () => {
    if (scrollTimer.current) clearInterval(scrollTimer.current);
    scrollTimer.current = null;
  };
  useEffect(() => stopAutoScroll, []);

  const update = useCallback((clientY: number) => {
    const s = session.current;
    if (!s) return;
    s.lastY = clientY;
    const { count: n, rowHeight: h, getScrollElement: getEl } = latest.current;
    const scrollTop = getEl?.()?.scrollTop ?? 0;
    const dy = clientY - s.startY + (scrollTop - s.startScroll);
    const to = Math.max(0, Math.min(n - 1, s.from + Math.round(dy / h)));
    if (to !== s.to) {
      s.to = to;
      setDrag({ from: s.from, to });
    }
  }, []);

  const onPointerDown = useCallback(
    (index: number) => (e: PointerEvent<HTMLElement>) => {
      if (e.button !== 0) return;
      e.preventDefault();
      e.currentTarget.setPointerCapture?.(e.pointerId);
      const el = latest.current.getScrollElement?.();
      session.current = { from: index, to: index, startY: e.clientY, startScroll: el?.scrollTop ?? 0, lastY: e.clientY };
      setDrag({ from: index, to: index });
    },
    []
  );

  const onPointerMove = useCallback(
    (e: PointerEvent<HTMLElement>) => {
      if (!session.current) return;
      update(e.clientY);
      const el = latest.current.getScrollElement?.();
      if (!el) return;
      const rect = el.getBoundingClientRect();
      const edge = 48;
      const speed = e.clientY < rect.top + edge ? -12 : e.clientY > rect.bottom - edge ? 12 : 0;
      stopAutoScroll();
      if (speed !== 0) {
        scrollTimer.current = setInterval(() => {
          el.scrollTop += speed;
          if (session.current) update(session.current.lastY);
        }, 16);
      }
    },
    [update]
  );

  const finish = useCallback((commit: boolean) => {
    stopAutoScroll();
    const s = session.current;
    session.current = null;
    setDrag(null);
    if (commit && s && s.to !== s.from) latest.current.onMove(s.from, s.to);
  }, []);

  /** Props for the drag handle of row `index`. */
  const handleProps = (index: number) => ({
    onPointerDown: onPointerDown(index),
    onPointerMove,
    onPointerUp: () => finish(true),
    onPointerCancel: () => finish(false),
    style: { touchAction: 'none' as const, cursor: drag ? 'grabbing' : 'grab' }
  });

  /** Keyboard reordering on the row itself: Alt+↑ / Alt+↓. Returns true when it moved. */
  const onRowKeyDown = (index: number, e: KeyboardEvent<HTMLElement>): boolean => {
    if (!e.altKey || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return false;
    const to = e.key === 'ArrowUp' ? index - 1 : index + 1;
    if (to < 0 || to >= latest.current.count) return false;
    e.preventDefault();
    latest.current.onMove(index, to);
    return true;
  };

  return { drag, handleProps, onRowKeyDown, offset: (index: number) => dragOffset(index, drag, rowHeight) };
}
