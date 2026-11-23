import { useAppearance, useResolvedTheme } from '@tfm/ui';
import { useEffect, useMemo, useRef, useState, type PointerEvent } from 'react';
import { t } from '@/i18n';
import { formatBytes } from '@/lib/format';
import type { SpeedSeries } from './model';

const HEIGHT = 150;
const PAD = { top: 12, right: 8, bottom: 20, left: 8 };

const speed = (bps: number) => `${formatBytes(bps)}/s`;

/** Round the axis maximum up to 1, 2 or 5 × 10^n bytes. */
function niceMax(value: number): number {
  if (value <= 0) return 1024;
  const exp = Math.pow(10, Math.floor(Math.log10(value)));
  const f = value / exp;
  return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * exp;
}

function path(points: [number, number][]): string {
  return points.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)},${y.toFixed(1)}`).join('');
}

/**
 * Live download / upload speed (SVG, one shared axis). Download wears the
 * accent colour, upload a contrasting hue plus a dashed line, so the two never
 * rely on colour alone; legend with the current values, hover crosshair.
 */
export function SpeedChart({ series, windowSeconds }: { series: SpeedSeries[]; windowSeconds: number }) {
  const ref = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(600);
  const [hover, setHover] = useState<number | null>(null);
  const accent = useAppearance((s) => s.accent);
  const { isLight } = useResolvedTheme();
  const warmAccent = accent === 'amber' || accent === 'sunset' || accent === 'rose';
  const upColor = warmAccent ? (isLight ? '#0369a1' : '#38bdf8') : isLight ? '#b45309' : '#fbbf24';

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const ro = new ResizeObserver(([entry]) => setWidth(Math.max(200, entry.contentRect.width)));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const geo = useMemo(() => {
    const now = series.at(-1)?.t ?? 0;
    const from = now - windowSeconds * 1000;
    const max = niceMax(Math.max(0, ...series.map((s) => Math.max(s.down, s.up))));
    const innerW = width - PAD.left - PAD.right;
    const innerH = HEIGHT - PAD.top - PAD.bottom;
    const x = (time: number) => PAD.left + ((time - from) / (now - from || 1)) * innerW;
    const y = (v: number) => PAD.top + innerH - (v / max) * innerH;
    const down = series.map((s) => [x(s.t), y(s.down)] as [number, number]);
    const up = series.map((s) => [x(s.t), y(s.up)] as [number, number]);
    const base = PAD.top + innerH;
    const area = down.length > 1 ? `${path(down)}L${down.at(-1)![0].toFixed(1)},${base}L${down[0][0].toFixed(1)},${base}Z` : '';
    return { max, x, y, down: path(down), up: path(up), area, base, innerH };
  }, [series, width, windowSeconds]);

  const last = series.at(-1);
  const hovered = hover !== null ? series[hover] : null;

  const onMove = (e: PointerEvent<SVGSVGElement>) => {
    if (series.length === 0) return;
    const rect = e.currentTarget.getBoundingClientRect();
    const px = e.clientX - rect.left;
    let best = 0;
    let bestD = Infinity;
    series.forEach((s, i) => {
      const d = Math.abs(geo.x(s.t) - px);
      if (d < bestD) {
        bestD = d;
        best = i;
      }
    });
    setHover(best);
  };

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-x-5 gap-y-1 text-xs">
        <span className="flex items-center gap-2 text-fg-muted">
          <span aria-hidden className="h-0.5 w-4 rounded-full bg-primary" />
          {t('Download')}
          <span className="font-semibold tabular-nums text-fg">{speed(last?.down ?? 0)}</span>
        </span>
        <span className="flex items-center gap-2 text-fg-muted">
          <svg aria-hidden width="16" height="2">
            <line x1="0" y1="1" x2="16" y2="1" stroke={upColor} strokeWidth="2" strokeDasharray="4 3" />
          </svg>
          {t('Upload')}
          <span className="font-semibold tabular-nums text-fg">{speed(last?.up ?? 0)}</span>
        </span>
        <span className="ml-auto text-fg-subtle">{t('Last {minutes} minutes', { minutes: Math.round(windowSeconds / 60) })}</span>
      </div>
      <div ref={ref} className="relative">
        <svg
          width={width}
          height={HEIGHT}
          role="img"
          aria-label={t('Transfer speed over time')}
          className="block touch-none"
          onPointerMove={onMove}
          onPointerLeave={() => setHover(null)}
        >
          {/* recessive grid: top (max) and middle */}
          {[0, 0.5].map((f) => (
            <line key={f} x1={PAD.left} x2={width - PAD.right} y1={PAD.top + geo.innerH * f} y2={PAD.top + geo.innerH * f} className="stroke-border" strokeDasharray="2 4" />
          ))}
          <line x1={PAD.left} x2={width - PAD.right} y1={geo.base} y2={geo.base} className="stroke-border" />
          <text x={width - PAD.right} y={PAD.top - 2} textAnchor="end" className="fill-fg-subtle text-[10px]">
            {speed(geo.max)}
          </text>
          {geo.area && <path d={geo.area} className="fill-primary/12" />}
          <path d={geo.down} fill="none" className="stroke-primary" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
          <path d={geo.up} fill="none" stroke={upColor} strokeWidth={2} strokeDasharray="5 4" strokeLinejoin="round" strokeLinecap="round" />
          {hovered && (
            <g>
              <line x1={geo.x(hovered.t)} x2={geo.x(hovered.t)} y1={PAD.top} y2={geo.base} className="stroke-fg-subtle" strokeWidth={1} />
              <circle cx={geo.x(hovered.t)} cy={geo.y(hovered.down)} r={4} className="fill-primary stroke-surface" strokeWidth={2} />
              <circle cx={geo.x(hovered.t)} cy={geo.y(hovered.up)} r={4} fill={upColor} className="stroke-surface" strokeWidth={2} />
            </g>
          )}
        </svg>
        {hovered && (
          <div
            className="pointer-events-none absolute top-2 z-10 rounded-lg border border-border bg-surface/95 px-2.5 py-1.5 text-xs shadow-elevated backdrop-blur"
            style={{ left: Math.min(Math.max(0, geo.x(hovered.t) + 10), width - 150) }}
          >
            <p className="text-fg-subtle">{new Date(hovered.t).toLocaleTimeString()}</p>
            <p className="tabular-nums text-fg">
              {t('Download')}: <b>{speed(hovered.down)}</b>
            </p>
            <p className="tabular-nums text-fg">
              {t('Upload')}: <b>{speed(hovered.up)}</b>
            </p>
          </div>
        )}
        {series.length < 2 && <p className="absolute inset-0 flex items-center justify-center text-xs text-fg-subtle">{t('Waiting for speed samples...')}</p>}
      </div>
    </div>
  );
}
