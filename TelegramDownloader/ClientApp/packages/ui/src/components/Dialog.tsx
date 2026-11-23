import { Dialog as DialogPrimitive } from 'radix-ui';
import { X } from 'lucide-react';
import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { OVERLAY } from '../lib/classes';
import { cn } from '../lib/cn';
import { uiStrings } from '../lib/strings';
import { IconButton } from './Button';

export const Dialog = DialogPrimitive.Root;
export const DialogTrigger = DialogPrimitive.Trigger;
export const DialogClose = DialogPrimitive.Close;

const DIALOG_SIZES = { sm: 'max-w-sm', md: 'max-w-lg', lg: 'max-w-2xl', xl: 'max-w-4xl', full: 'max-w-[min(96vw,80rem)]' } as const;

export interface DialogContentProps extends Omit<ComponentPropsWithoutRef<typeof DialogPrimitive.Content>, 'title'> {
  title?: ReactNode;
  description?: ReactNode;
  size?: keyof typeof DIALOG_SIZES;
  /** Footer (buttons), right aligned. */
  footer?: ReactNode;
  hideClose?: boolean;
}

export const DialogContent = forwardRef<HTMLDivElement, DialogContentProps>(function DialogContent(
  { className, children, title, description, size = 'md', footer, hideClose, ...props },
  ref
) {
  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className={OVERLAY} />
      <DialogPrimitive.Content
        ref={ref}
        {...(description ? {} : { 'aria-describedby': undefined })}
        className={cn(
          'glass-strong fixed left-1/2 top-1/2 z-50 flex max-h-[min(90dvh,56rem)] w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 flex-col',
          'rounded-2xl border border-border shadow-elevated outline-none',
          'data-[state=open]:animate-scale-in data-[state=closed]:animate-scale-out',
          DIALOG_SIZES[size],
          className
        )}
        {...props}
      >
        {(title || !hideClose) && (
          <div className="flex items-start gap-3 px-5 pb-2 pt-5">
            <div className="flex min-w-0 flex-1 flex-col gap-1">
              {title ? (
                <DialogPrimitive.Title className="text-lg font-semibold tracking-tight text-fg">{title}</DialogPrimitive.Title>
              ) : (
                <DialogPrimitive.Title className="sr-only">{uiStrings.close}</DialogPrimitive.Title>
              )}
              {description && (
                <DialogPrimitive.Description className="text-sm text-fg-muted">{description}</DialogPrimitive.Description>
              )}
            </div>
            {!hideClose && (
              <DialogPrimitive.Close asChild>
                <IconButton label={uiStrings.close} size="sm" className="-mr-1 -mt-1">
                  <X />
                </IconButton>
              </DialogPrimitive.Close>
            )}
          </div>
        )}
        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-3">{children}</div>
        {footer && <div className="flex flex-wrap items-center justify-end gap-2 border-t border-border px-5 py-3.5">{footer}</div>}
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  );
});
