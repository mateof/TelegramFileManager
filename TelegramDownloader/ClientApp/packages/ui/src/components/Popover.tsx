import { Popover as PopoverPrimitive } from 'radix-ui';
import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { cn } from '../lib/cn';

export const Popover = PopoverPrimitive.Root;
export const PopoverTrigger = PopoverPrimitive.Trigger;
export const PopoverAnchor = PopoverPrimitive.Anchor;
export const PopoverClose = PopoverPrimitive.Close;

export const PopoverContent = forwardRef<HTMLDivElement, ComponentPropsWithoutRef<typeof PopoverPrimitive.Content>>(
  function PopoverContent({ className, sideOffset = 8, align = 'center', collisionPadding = 8, ...props }, ref) {
    return (
      <PopoverPrimitive.Portal>
        <PopoverPrimitive.Content
          ref={ref}
          sideOffset={sideOffset}
          align={align}
          collisionPadding={collisionPadding}
          className={cn(
            'glass-strong z-50 w-72 rounded-xl border border-border p-3 text-fg shadow-elevated outline-none',
            'data-[state=open]:animate-pop-in data-[state=closed]:animate-fade-out',
            className
          )}
          {...props}
        />
      </PopoverPrimitive.Portal>
    );
  }
);
