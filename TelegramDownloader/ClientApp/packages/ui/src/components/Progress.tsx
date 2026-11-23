import { Progress as ProgressPrimitive } from 'radix-ui';
import { cn } from '../lib/cn';

export interface ProgressProps {
  /** 0-100; undefined = indeterminate. */
  value?: number;
  className?: string;
  tone?: 'accent' | 'success' | 'warning' | 'danger';
  size?: 'xs' | 'sm' | 'md';
  'aria-label'?: string;
}

const PROGRESS_TONES = { accent: 'bg-primary', success: 'bg-success', warning: 'bg-warning', danger: 'bg-danger' } as const;
const PROGRESS_SIZES = { xs: 'h-1', sm: 'h-1.5', md: 'h-2.5' } as const;

export function Progress({ value, className, tone = 'accent', size = 'sm', ...aria }: ProgressProps) {
  const indeterminate = value === undefined;
  const clamped = Math.max(0, Math.min(100, value ?? 0));
  return (
    <ProgressPrimitive.Root
      value={indeterminate ? null : clamped}
      className={cn('relative w-full overflow-hidden rounded-full bg-surface-3/70', PROGRESS_SIZES[size], className)}
      {...aria}
    >
      <ProgressPrimitive.Indicator
        className={cn(
          'h-full rounded-full transition-transform duration-300 ease-out',
          PROGRESS_TONES[tone],
          indeterminate && 'w-1/3 animate-progress'
        )}
        style={indeterminate ? undefined : { transform: `translateX(-${100 - clamped}%)` }}
      />
    </ProgressPrimitive.Root>
  );
}
