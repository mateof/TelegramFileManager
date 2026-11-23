import { AlertDialog as AlertPrimitive } from 'radix-ui';
import { useSyncExternalStore, type ReactNode } from 'react';
import { OVERLAY } from '../lib/classes';
import { cn } from '../lib/cn';
import { uiStrings } from '../lib/strings';
import { buttonClasses } from './Button';

export const AlertDialog = AlertPrimitive.Root;
export const AlertDialogTrigger = AlertPrimitive.Trigger;

export interface AlertDialogContentProps {
  title: ReactNode;
  description?: ReactNode;
  confirmLabel?: ReactNode;
  cancelLabel?: ReactNode;
  destructive?: boolean;
  onConfirm?: () => void;
  onCancel?: () => void;
  children?: ReactNode;
}

export function AlertDialogContent({
  title,
  description,
  confirmLabel,
  cancelLabel,
  destructive,
  onConfirm,
  onCancel,
  children
}: AlertDialogContentProps) {
  return (
    <AlertPrimitive.Portal>
      <AlertPrimitive.Overlay className={OVERLAY} />
      <AlertPrimitive.Content
        className={cn(
          'glass-strong fixed left-1/2 top-1/2 z-50 flex w-[calc(100vw-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 flex-col gap-4',
          'rounded-2xl border border-border p-5 shadow-elevated outline-none data-[state=open]:animate-scale-in data-[state=closed]:animate-scale-out'
        )}
      >
        <div className="flex flex-col gap-1.5">
          <AlertPrimitive.Title className="text-lg font-semibold tracking-tight text-fg">{title}</AlertPrimitive.Title>
          {description ? (
            <AlertPrimitive.Description className="text-sm text-fg-muted">{description}</AlertPrimitive.Description>
          ) : (
            <AlertPrimitive.Description className="sr-only">{title}</AlertPrimitive.Description>
          )}
        </div>
        {children}
        <div className="flex flex-wrap justify-end gap-2">
          <AlertPrimitive.Cancel className={buttonClasses('secondary', 'md')} onClick={onCancel}>
            {cancelLabel ?? uiStrings.cancel}
          </AlertPrimitive.Cancel>
          <AlertPrimitive.Action className={buttonClasses(destructive ? 'danger' : 'primary', 'md')} onClick={onConfirm}>
            {confirmLabel ?? uiStrings.confirm}
          </AlertPrimitive.Action>
        </div>
      </AlertPrimitive.Content>
    </AlertPrimitive.Portal>
  );
}

export interface ConfirmOptions {
  title: ReactNode;
  description?: ReactNode;
  confirmLabel?: ReactNode;
  cancelLabel?: ReactNode;
  destructive?: boolean;
}

interface PendingConfirm extends ConfirmOptions {
  id: number;
  resolve: (ok: boolean) => void;
}

let pending: PendingConfirm | null = null;
let nextId = 1;
const confirmListeners = new Set<() => void>();
const emitConfirm = () => confirmListeners.forEach((l) => l());

/**
 * Imperative confirmation dialog: `if (await confirm({ title: t('Delete?'), destructive: true })) ...`.
 * Requires `<ConfirmHost />` mounted once (the app's providers do it).
 */
export function confirm(options: ConfirmOptions): Promise<boolean> {
  pending?.resolve(false);
  return new Promise<boolean>((resolve) => {
    pending = { ...options, id: nextId++, resolve };
    emitConfirm();
  });
}

function settle(ok: boolean) {
  if (!pending) return;
  const p = pending;
  pending = null;
  p.resolve(ok);
  emitConfirm();
}

export function ConfirmHost() {
  const current = useSyncExternalStore(
    (cb) => {
      confirmListeners.add(cb);
      return () => confirmListeners.delete(cb);
    },
    () => pending,
    () => null
  );
  return (
    <AlertDialog open={!!current} onOpenChange={(open) => !open && settle(false)}>
      {current && (
        <AlertDialogContent
          key={current.id}
          title={current.title}
          description={current.description}
          confirmLabel={current.confirmLabel}
          cancelLabel={current.cancelLabel}
          destructive={current.destructive}
          onConfirm={() => settle(true)}
          onCancel={() => settle(false)}
        />
      )}
    </AlertDialog>
  );
}
