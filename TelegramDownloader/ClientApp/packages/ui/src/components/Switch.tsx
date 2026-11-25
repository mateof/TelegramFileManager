import { Switch as SwitchPrimitive } from 'radix-ui';
import { forwardRef, useId, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { cn } from '../lib/cn';

export interface SwitchProps extends ComponentPropsWithoutRef<typeof SwitchPrimitive.Root> {
  label?: ReactNode;
  description?: ReactNode;
}

export const Switch = forwardRef<HTMLButtonElement, SwitchProps>(function Switch(
  { className, label, description, id, ...props },
  ref
) {
  const autoId = useId();
  const switchId = id ?? autoId;
  const control = (
    <SwitchPrimitive.Root
      ref={ref}
      id={switchId}
      className={cn(
        'peer inline-flex h-5.5 w-10 shrink-0 items-center rounded-full border border-transparent bg-surface-3 p-0.5 transition-colors duration-200',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring data-[state=checked]:bg-primary disabled:opacity-50',
        !label && className
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb className="block size-4.5 rounded-full bg-white shadow-soft transition-transform duration-200 ease-spring data-[state=checked]:translate-x-4.5" />
    </SwitchPrimitive.Root>
  );
  if (!label) return control;
  return (
    <div className={cn('flex items-start justify-between gap-4', className)}>
      <label htmlFor={switchId} className="flex cursor-pointer flex-col gap-0.5">
        <span className="text-sm font-medium text-fg">{label}</span>
        {description && <span className="text-xs text-fg-muted">{description}</span>}
      </label>
      {control}
    </div>
  );
});
