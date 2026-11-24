import { forwardRef, type HTMLAttributes } from 'react';
import { cn } from '../lib/cn';

export interface CardProps extends HTMLAttributes<HTMLDivElement> {
  /** Frosted translucent surface instead of an opaque one; `flat` skips the blur, for cards repeated down a list. */
  glass?: boolean | 'flat';
  /** Lift + glow on hover (for clickable cards). */
  interactive?: boolean;
}

export const Card = forwardRef<HTMLDivElement, CardProps>(function Card({ glass, interactive, className, ...props }, ref) {
  return (
    <div
      ref={ref}
      className={cn(
        'rounded-2xl border border-border shadow-soft',
        glass === 'flat' ? 'glass-flat' : glass ? 'glass' : 'bg-surface',
        interactive &&
          'transition-[transform,box-shadow,border-color] duration-200 hover:-translate-y-0.5 hover:border-accent-500/40 hover:shadow-glow',
        className
      )}
      {...props}
    />
  );
});

export function CardHeader({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex flex-col gap-1 p-5 pb-3', className)} {...props} />;
}

export function CardTitle({ className, ...props }: HTMLAttributes<HTMLHeadingElement>) {
  return <h3 className={cn('text-base font-semibold tracking-tight text-fg', className)} {...props} />;
}

export function CardDescription({ className, ...props }: HTMLAttributes<HTMLParagraphElement>) {
  return <p className={cn('text-sm text-fg-muted', className)} {...props} />;
}

export function CardContent({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('p-5 pt-0', className)} {...props} />;
}

export function CardFooter({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex items-center gap-2 border-t border-border px-5 py-3', className)} {...props} />;
}
