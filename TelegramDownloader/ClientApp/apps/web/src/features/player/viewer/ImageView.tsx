import { Spinner } from '@tfm/ui';
import { ImageOff, Minus, Plus, RotateCcw } from 'lucide-react';
import { useEffect, useRef, useState, type PointerEvent, type WheelEvent } from 'react';
import { t } from '@/i18n';
import type { ViewerItem } from '../public';
import { ViewerButton } from './ViewerButton';

const MIN = 1;
const MAX = 8;
const SWIPE_PX = 60;

interface View {
  scale: number;
  x: number;
  y: number;
}

const clampScale = (s: number) => Math.max(MIN, Math.min(MAX, s));

/** Zoom keeping the point `p` (relative to the center) still. */
function zoomAt(view: View, nextScale: number, px: number, py: number): View {
  const scale = clampScale(nextScale);
  if (scale === MIN) return { scale, x: 0, y: 0 };
  const k = scale / view.scale;
  return { scale, x: px - (px - view.x) * k, y: py - (py - view.y) * k };
}

/**
 * One image of the gallery: wheel / pinch / double-tap zoom, drag to pan when
 * zoomed, swipe to browse when not, +/-/0 keys.
 */
export function ImageView({ item, onPrev, onNext }: { item: ViewerItem; onPrev?: () => void; onNext?: () => void }) {
  const ref = useRef<HTMLDivElement>(null);
  const [view, setView] = useState<View>({ scale: 1, x: 0, y: 0 });
  const [loaded, setLoaded] = useState(false);
  const [failed, setFailed] = useState(false);
  const pointers = useRef(new Map<number, { x: number; y: number }>());
  const gesture = useRef<{ startX: number; startY: number; view: View; dist?: number; moved: boolean } | null>(null);
  const lastTap = useRef(0);
  const [active, setActive] = useState(false);

  const center = (clientX: number, clientY: number) => {
    const r = ref.current?.getBoundingClientRect();
    return r ? { x: clientX - (r.left + r.width / 2), y: clientY - (r.top + r.height / 2) } : { x: 0, y: 0 };
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey) return;
      if (e.key === '+' || e.key === '=') setView((v) => zoomAt(v, v.scale * 1.5, 0, 0));
      else if (e.key === '-') setView((v) => zoomAt(v, v.scale / 1.5, 0, 0));
      else if (e.key === '0') setView({ scale: 1, x: 0, y: 0 });
      else return;
      e.preventDefault();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const onWheel = (e: WheelEvent<HTMLDivElement>) => {
    const p = center(e.clientX, e.clientY);
    const factor = Math.exp(-e.deltaY * 0.0015);
    setView((v) => zoomAt(v, v.scale * factor, p.x, p.y));
  };

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    e.currentTarget.setPointerCapture?.(e.pointerId);
    pointers.current.set(e.pointerId, { x: e.clientX, y: e.clientY });
    setActive(true);
    const pts = [...pointers.current.values()];
    if (pts.length === 2) {
      gesture.current = { startX: (pts[0].x + pts[1].x) / 2, startY: (pts[0].y + pts[1].y) / 2, view, dist: Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y), moved: true };
    } else {
      gesture.current = { startX: e.clientX, startY: e.clientY, view, moved: false };
    }
  };

  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    if (!pointers.current.has(e.pointerId) || !gesture.current) return;
    pointers.current.set(e.pointerId, { x: e.clientX, y: e.clientY });
    const g = gesture.current;
    const pts = [...pointers.current.values()];
    if (pts.length >= 2 && g.dist) {
      const dist = Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y);
      const mid = center((pts[0].x + pts[1].x) / 2, (pts[0].y + pts[1].y) / 2);
      setView(zoomAt(g.view, g.view.scale * (dist / g.dist), mid.x, mid.y));
      return;
    }
    const dx = e.clientX - g.startX;
    const dy = e.clientY - g.startY;
    if (Math.abs(dx) + Math.abs(dy) > 4) g.moved = true;
    if (g.view.scale > 1) setView({ ...g.view, x: g.view.x + dx, y: g.view.y + dy });
    else setView({ scale: 1, x: dx, y: 0 }); // swipe preview
  };

  const onPointerUp = (e: PointerEvent<HTMLDivElement>) => {
    const g = gesture.current;
    pointers.current.delete(e.pointerId);
    if (pointers.current.size > 0) {
      // Pinch ended with one finger down: continue as a pan from here
      const [p] = [...pointers.current.values()];
      gesture.current = { startX: p.x, startY: p.y, view, moved: true };
      return;
    }
    gesture.current = null;
    setActive(false);
    if (!g) return;
    if (g.view.scale <= 1) {
      const dx = e.clientX - g.startX;
      setView({ scale: 1, x: 0, y: 0 });
      if (dx > SWIPE_PX && onPrev) onPrev();
      else if (dx < -SWIPE_PX && onNext) onNext();
    }
    if (!g.moved) {
      // Double tap / double click toggles zoom at that point
      const now = Date.now();
      if (now - lastTap.current < 300) {
        const p = center(e.clientX, e.clientY);
        setView((v) => (v.scale > 1 ? { scale: 1, x: 0, y: 0 } : zoomAt(v, 2.5, p.x, p.y)));
        lastTap.current = 0;
      } else lastTap.current = now;
    }
  };

  const zoomed = view.scale > 1;
  const swiping = !zoomed && view.x !== 0;

  return (
    <div className="relative flex size-full items-center justify-center overflow-hidden">
      <div
        ref={ref}
        role="img"
        aria-label={item.name}
        className="flex size-full touch-none select-none items-center justify-center"
        style={{ cursor: zoomed ? (active ? 'grabbing' : 'grab') : 'zoom-in' }}
        onWheel={onWheel}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
      >
        {failed ? (
          <div className="flex flex-col items-center gap-2 text-white/70">
            <ImageOff className="size-10" />
            <p className="text-sm">{t('The image could not be loaded')}</p>
          </div>
        ) : (
          <img
            src={item.url}
            alt={item.name}
            draggable={false}
            onLoad={() => setLoaded(true)}
            onError={() => setFailed(true)}
            className="max-h-full max-w-full object-contain will-change-transform"
            style={{
              transform: `translate(${view.x}px, ${view.y}px) scale(${view.scale})`,
              transition: active && !swiping ? 'none' : 'transform 180ms cubic-bezier(0.22,1,0.36,1)',
              opacity: loaded ? (swiping ? Math.max(0.4, 1 - Math.abs(view.x) / 600) : 1) : 0
            }}
          />
        )}
        {!loaded && !failed && (
          <div className="absolute inset-0 flex items-center justify-center">
            {item.thumbnailUrl && <img src={item.thumbnailUrl} alt="" className="absolute max-h-full max-w-full scale-95 object-contain opacity-50 blur-md" />}
            <Spinner size="lg" className="text-white" />
          </div>
        )}
      </div>
      <div className="absolute bottom-[max(1rem,env(safe-area-inset-bottom))] left-1/2 flex -translate-x-1/2 items-center gap-2 rounded-full bg-black/40 p-1.5 backdrop-blur-md">
        <ViewerButton label={t('Zoom out')} onClick={() => setView((v) => zoomAt(v, v.scale / 1.5, 0, 0))} className="size-9">
          <Minus />
        </ViewerButton>
        <span className="w-12 text-center text-xs tabular-nums text-white/80">{Math.round(view.scale * 100)}%</span>
        <ViewerButton label={t('Zoom in')} onClick={() => setView((v) => zoomAt(v, v.scale * 1.5, 0, 0))} className="size-9">
          <Plus />
        </ViewerButton>
        {zoomed && (
          <ViewerButton label={t('Reset zoom')} onClick={() => setView({ scale: 1, x: 0, y: 0 })} className="size-9">
            <RotateCcw />
          </ViewerButton>
        )}
      </div>
    </div>
  );
}
