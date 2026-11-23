import { cn } from '../lib/cn';
import { uiStrings } from '../lib/strings';

const SIZES = { xs: 'size-3', sm: 'size-4', md: 'size-5', lg: 'size-8' } as const;

export interface SpinnerProps {
  size?: keyof typeof SIZES;
  className?: string;
  /** Accessible label (defaults to "Loading..."). */
  label?: string;
}

export function Spinner({ size = 'md', className, label }: SpinnerProps) {
  return (
    <svg
      role="status"
      aria-label={label ?? uiStrings.loading}
      viewBox="0 0 24 24"
      fill="none"
      className={cn('animate-spin text-current', SIZES[size], className)}
    >
      <circle cx="12" cy="12" r="9.5" stroke="currentColor" strokeOpacity="0.2" strokeWidth="3" />
      <path d="M21.5 12a9.5 9.5 0 0 0-9.5-9.5" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    </svg>
  );
}
