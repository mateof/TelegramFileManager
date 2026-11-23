import { cn } from '@tfm/ui';
import { useId } from 'react';

export interface ChartSeries {
  label: string;
  values: number[];
  /** CSS colour (a theme variable such as `var(--color-accent-400)`). */
  color: string;
}

/**
 * Lightweight live area/line chart in SVG: no axes library, scales to its
 * container (`preserveAspectRatio="none"`), one gradient-filled area per series.
 */
export function LineChart({
  series,
  max,
  capacity,
  height = 96,
  className,
  label,
  formatValue
}: {
  series: ChartSeries[];
  /** Upper bound of the Y axis; defaults to the largest value (min 1). */
  max?: number;
  /** Number of samples the X axis holds (older samples scroll out on the left). */
  capacity: number;
  height?: number;
  className?: string;
  label: string;
  formatValue?: (v: number) => string;
}) {
  const id = useId().replace(/:/g, '');
  const width = 300;
  const top = max ?? Math.max(1, ...series.flatMap((s) => s.values)) * 1.15;
  const step = width / Math.max(1, capacity - 1);

  const points = (values: number[]) => {
    const offset = capacity - values.length;
    return values.map((v, i) => [(offset + i) * step, height - (Math.min(v, top) / top) * (height - 4) - 2] as const);
  };

  const last = series.map((s) => s.values[s.values.length - 1]);
  const summary = series.map((s, i) => `${s.label}: ${formatValue ? formatValue(last[i] ?? 0) : (last[i] ?? 0)}`).join(', ');

  return (
    <div className={cn('relative', className)}>
      <svg role="img" aria-label={`${label}. ${summary}`} viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" className="block h-full w-full overflow-visible" style={{ height }}>
        <defs>
          {series.map((s, i) => (
            <linearGradient key={i} id={`${id}-g${i}`} x1="0" x2="0" y1="0" y2="1">
              <stop offset="0%" stopColor={s.color} stopOpacity="0.35" />
              <stop offset="100%" stopColor={s.color} stopOpacity="0" />
            </linearGradient>
          ))}
        </defs>
        {[0.25, 0.5, 0.75].map((f) => (
          <line key={f} x1="0" x2={width} y1={height * f} y2={height * f} stroke="var(--color-border)" strokeDasharray="2 4" strokeWidth="1" vectorEffect="non-scaling-stroke" />
        ))}
        {series.map((s, i) => {
          const pts = points(s.values);
          if (pts.length < 2) return null;
          const line = pts.map(([x, y], j) => `${j === 0 ? 'M' : 'L'}${x.toFixed(2)} ${y.toFixed(2)}`).join(' ');
          const first = pts[0]!;
          const lastPt = pts[pts.length - 1]!;
          const area = `${line} L${lastPt[0].toFixed(2)} ${height} L${first[0].toFixed(2)} ${height} Z`;
          return (
            <g key={s.label}>
              <path d={area} fill={`url(#${id}-g${i})`} />
              <path d={line} fill="none" stroke={s.color} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
            </g>
          );
        })}
      </svg>
    </div>
  );
}

/** Legend dots for a chart. */
export function ChartLegend({ items }: { items: { label: string; color: string; value?: string }[] }) {
  return (
    <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-fg-muted">
      {items.map((i) => (
        <li key={i.label} className="flex items-center gap-1.5">
          <span className="size-2 rounded-full" style={{ background: i.color }} />
          {i.label}
          {i.value && <span className="font-medium tabular-nums text-fg">{i.value}</span>}
        </li>
      ))}
    </ul>
  );
}
