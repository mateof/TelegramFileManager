import { Checkbox as CheckboxPrimitive } from 'radix-ui';
import { Check, Minus } from 'lucide-react';
import { forwardRef, useId, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { cn } from '../lib/cn';

export interface CheckboxProps extends ComponentPropsWithoutRef<typeof CheckboxPrimitive.Root> {
  label?: ReactNode;
}

export const Checkbox = forwardRef<HTMLButtonElement, CheckboxProps>(function Checkbox(
  { className, label, id, ...props },
  ref
) {
  const autoId = useId();
  const boxId = id ?? autoId;
  const box = (
    <CheckboxPrimitive.Root
      ref={ref}
      id={boxId}
      className={cn(
        'peer flex size-4.5 shrink-0 items-center justify-center rounded-[5px] border border-border-strong bg-surface-2 transition-colors',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
        'data-[state=checked]:border-primary data-[state=checked]:bg-primary data-[state=indeterminate]:border-primary data-[state=indeterminate]:bg-primary disabled:opacity-50',
        !label && className
      )}
      {...props}
    >
      <CheckboxPrimitive.Indicator className="text-on-accent">
        {props.checked === 'indeterminate' ? <Minus className="size-3.5" strokeWidth={3} /> : <Check className="size-3.5" strokeWidth={3} />}
      </CheckboxPrimitive.Indicator>
    </CheckboxPrimitive.Root>
  );
  if (!label) return box;
  return (
    <div className={cn('flex items-center gap-2.5', className)}>
      {box}
      <label htmlFor={boxId} className="cursor-pointer text-sm text-fg">
        {label}
      </label>
    </div>
  );
});
