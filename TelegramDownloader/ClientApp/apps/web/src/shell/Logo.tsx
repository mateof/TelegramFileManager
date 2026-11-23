import { cn } from '@tfm/ui';

/** App mark: a folder with a paper plane, in the accent colour. */
export function Logo({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 64 64" aria-hidden className={cn('shrink-0 drop-shadow-[0_4px_12px_var(--color-primary-soft)]', className)}>
      <defs>
        <linearGradient id="tfm-logo" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="var(--color-accent-400)" />
          <stop offset="1" stopColor="var(--color-accent-600)" />
        </linearGradient>
      </defs>
      <rect width="64" height="64" rx="16" fill="url(#tfm-logo)" />
      <path d="M15 22a5 5 0 0 1 5-5h9l4 5h11a5 5 0 0 1 5 5v15a5 5 0 0 1-5 5H20a5 5 0 0 1-5-5z" fill="#fff" fillOpacity=".92" />
      <path d="M27 33l12-5-4 12-3-4z" fill="var(--color-accent-600)" />
    </svg>
  );
}
