import { type HTMLAttributes } from 'react';
import { cn } from '../lib/cn';

export function Skeleton({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      aria-hidden
      className={cn(
        'animate-shimmer rounded-md bg-[length:200%_100%]',
        'bg-[linear-gradient(90deg,var(--color-surface-2)_0%,var(--color-surface-3)_50%,var(--color-surface-2)_100%)] opacity-60',
        className
      )}
      {...props}
    />
  );
}
