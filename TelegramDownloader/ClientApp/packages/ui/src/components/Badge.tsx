import { type HTMLAttributes } from 'react';
import { cn } from '../lib/cn';

export type BadgeTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info';

const BADGE_TONES: Record<BadgeTone, string> = {
  neutral: 'bg-surface-2 text-fg-muted border-border',
  accent: 'bg-primary-soft text-accent-300 border-accent-500/30',
  success: 'bg-success-soft text-success border-success/30',
  warning: 'bg-warning-soft text-warning border-warning/30',
  danger: 'bg-danger-soft text-danger border-danger/30',
  info: 'bg-info-soft text-info border-info/30'
};

export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  tone?: BadgeTone;
  /** Small solid dot before the text. */
  dot?: boolean;
}

export function Badge({ tone = 'neutral', dot, className, children, ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 whitespace-nowrap rounded-full border px-2 py-0.5 text-xs font-medium',
        BADGE_TONES[tone],
        className
      )}
      {...props}
    >
      {dot && <span aria-hidden className="size-1.5 rounded-full bg-current" />}
      {children}
    </span>
  );
}

/** Tiny numeric counter (notifications, transfers). Hidden when 0. */
export function CountBadge({ count, max = 99, className }: { count: number; max?: number; className?: string }) {
  if (count <= 0) return null;
  return (
    <span
      className={cn(
        'pointer-events-none inline-flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-semibold leading-none text-on-accent shadow-soft',
        className
      )}
    >
      {count > max ? `${max}+` : count}
    </span>
  );
}
