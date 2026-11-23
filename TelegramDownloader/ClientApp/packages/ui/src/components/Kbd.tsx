import { type HTMLAttributes } from 'react';
import { cn } from '../lib/cn';

export function Kbd({ className, ...props }: HTMLAttributes<HTMLElement>) {
  return (
    <kbd
      className={cn(
        'inline-flex h-5 min-w-5 items-center justify-center rounded border border-border bg-surface-2 px-1 font-mono text-[11px] font-medium text-fg-muted shadow-[inset_0_-1px_0_var(--color-border)]',
        className
      )}
      {...props}
    />
  );
}
