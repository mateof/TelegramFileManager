import { ScrollArea as ScrollPrimitive } from 'radix-ui';
import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { cn } from '../lib/cn';

export interface ScrollAreaProps extends ComponentPropsWithoutRef<typeof ScrollPrimitive.Root> {
  viewportClassName?: string;
  orientation?: 'vertical' | 'horizontal' | 'both';
}

/** Overlay scrollbars that match the theme. */
export const ScrollArea = forwardRef<HTMLDivElement, ScrollAreaProps>(function ScrollArea(
  { className, viewportClassName, children, orientation = 'vertical', ...props },
  ref
) {
  return (
    <ScrollPrimitive.Root ref={ref} type="hover" className={cn('relative overflow-hidden', className)} {...props}>
      <ScrollPrimitive.Viewport className={cn('size-full rounded-[inherit] [&>div]:block!', viewportClassName)}>
        {children}
      </ScrollPrimitive.Viewport>
      {(orientation === 'vertical' || orientation === 'both') && <ScrollBar orientation="vertical" />}
      {(orientation === 'horizontal' || orientation === 'both') && <ScrollBar orientation="horizontal" />}
      <ScrollPrimitive.Corner />
    </ScrollPrimitive.Root>
  );
});

function ScrollBar({ orientation }: { orientation: 'vertical' | 'horizontal' }) {
  return (
    <ScrollPrimitive.Scrollbar
      orientation={orientation}
      className={cn(
        'flex touch-none select-none p-0.5 transition-opacity duration-200 data-[state=hidden]:opacity-0',
        orientation === 'vertical' ? 'h-full w-2.5' : 'h-2.5 flex-col'
      )}
    >
      <ScrollPrimitive.Thumb className="relative flex-1 rounded-full bg-neutral-600/60 hover:bg-neutral-500/70" />
    </ScrollPrimitive.Scrollbar>
  );
}
