import { Tooltip as TooltipPrimitive } from 'radix-ui';
import { type ReactNode } from 'react';

export const TooltipProvider = TooltipPrimitive.Provider;

export interface TooltipProps {
  content: ReactNode;
  children: ReactNode;
  side?: 'top' | 'right' | 'bottom' | 'left';
  /** Keyboard shortcut shown next to the text. */
  shortcut?: ReactNode;
  delayDuration?: number;
}

/** Wraps a single focusable child. Mount `TooltipProvider` once (the app's providers do it). */
export function Tooltip({ content, children, side = 'top', shortcut, delayDuration }: TooltipProps) {
  return (
    <TooltipPrimitive.Root delayDuration={delayDuration}>
      <TooltipPrimitive.Trigger asChild>{children}</TooltipPrimitive.Trigger>
      <TooltipPrimitive.Portal>
        <TooltipPrimitive.Content
          side={side}
          sideOffset={6}
          className="z-[60] flex items-center gap-2 rounded-lg border border-border bg-neutral-800 px-2.5 py-1.5 text-xs font-medium text-fg shadow-elevated data-[state=delayed-open]:animate-pop-in data-[state=closed]:animate-fade-out"
        >
          {content}
          {shortcut && <span className="text-fg-subtle">{shortcut}</span>}
        </TooltipPrimitive.Content>
      </TooltipPrimitive.Portal>
    </TooltipPrimitive.Root>
  );
}
