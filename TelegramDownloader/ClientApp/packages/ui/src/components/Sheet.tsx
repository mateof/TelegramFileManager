import { Dialog as DialogPrimitive } from 'radix-ui';
import { X } from 'lucide-react';
import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { OVERLAY } from '../lib/classes';
import { cn } from '../lib/cn';
import { uiStrings } from '../lib/strings';
import { IconButton } from './Button';

export const Sheet = DialogPrimitive.Root;
export const SheetTrigger = DialogPrimitive.Trigger;
export const SheetClose = DialogPrimitive.Close;

const SHEET_SIDES = {
  right:
    'inset-y-0 right-0 h-full w-[min(26rem,92vw)] border-l rounded-l-2xl data-[state=open]:animate-slide-in-right data-[state=closed]:animate-slide-out-right',
  left: 'inset-y-0 left-0 h-full w-[min(20rem,86vw)] border-r rounded-r-2xl data-[state=open]:animate-slide-in-left data-[state=closed]:animate-slide-out-left',
  bottom:
    'inset-x-0 bottom-0 max-h-[85dvh] border-t rounded-t-3xl pb-[env(safe-area-inset-bottom)] data-[state=open]:animate-slide-in-bottom data-[state=closed]:animate-slide-out-bottom'
} as const;

export interface SheetContentProps extends Omit<ComponentPropsWithoutRef<typeof DialogPrimitive.Content>, 'title'> {
  side?: keyof typeof SHEET_SIDES;
  title?: ReactNode;
  description?: ReactNode;
  /** Visually hide the header but keep it for screen readers. */
  hideHeader?: boolean;
}

/** Side panel (or bottom drawer on mobile with `side="bottom"`). */
export const SheetContent = forwardRef<HTMLDivElement, SheetContentProps>(function SheetContent(
  { side = 'right', className, children, title, description, hideHeader, ...props },
  ref
) {
  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className={OVERLAY} />
      <DialogPrimitive.Content
        ref={ref}
        className={cn('glass-strong fixed z-50 flex flex-col border-border shadow-elevated outline-none', SHEET_SIDES[side], className)}
        {...(description ? {} : { 'aria-describedby': undefined })}
        {...props}
      >
        {side === 'bottom' && <div aria-hidden className="mx-auto mt-2.5 h-1 w-10 shrink-0 rounded-full bg-surface-3" />}
        <div className={cn('flex items-center gap-3 px-5 pb-2 pt-4', hideHeader && 'sr-only')}>
          <div className="flex min-w-0 flex-1 flex-col gap-0.5">
            <DialogPrimitive.Title className="text-base font-semibold text-fg">{title ?? ''}</DialogPrimitive.Title>
            {description && <DialogPrimitive.Description className="text-sm text-fg-muted">{description}</DialogPrimitive.Description>}
          </div>
          <DialogPrimitive.Close asChild>
            <IconButton label={uiStrings.close} size="sm">
              <X />
            </IconButton>
          </DialogPrimitive.Close>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto">{children}</div>
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  );
});
