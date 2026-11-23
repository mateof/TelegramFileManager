import { RadioGroup as RadioPrimitive } from 'radix-ui';
import { useId, type ReactNode } from 'react';
import { cn } from '../lib/cn';
import type { SelectOption } from './Select';

export interface RadioGroupProps<V extends string = string> {
  value?: V;
  defaultValue?: V;
  onValueChange?: (value: V) => void;
  options: (SelectOption<V> & { description?: ReactNode })[];
  className?: string;
  orientation?: 'horizontal' | 'vertical';
  'aria-label'?: string;
}

export function RadioGroup<V extends string = string>({
  options,
  onValueChange,
  className,
  orientation = 'vertical',
  ...props
}: RadioGroupProps<V>) {
  const baseId = useId();
  return (
    <RadioPrimitive.Root
      {...props}
      orientation={orientation}
      onValueChange={(v) => onValueChange?.(v as V)}
      className={cn('flex gap-3', orientation === 'vertical' ? 'flex-col' : 'flex-row flex-wrap', className)}
    >
      {options.map((o) => {
        const id = `${baseId}-${o.value}`;
        return (
          <div key={o.value} className="flex items-start gap-2.5">
            <RadioPrimitive.Item
              id={id}
              value={o.value}
              disabled={o.disabled}
              className="mt-0.5 flex size-4.5 shrink-0 items-center justify-center rounded-full border border-border-strong bg-surface-2 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring data-[state=checked]:border-primary disabled:opacity-50"
            >
              <RadioPrimitive.Indicator className="size-2.5 rounded-full bg-primary data-[state=checked]:animate-pop-in" />
            </RadioPrimitive.Item>
            <label htmlFor={id} className="flex cursor-pointer flex-col gap-0.5">
              <span className="text-sm text-fg">{o.label}</span>
              {o.description && <span className="text-xs text-fg-muted">{o.description}</span>}
            </label>
          </div>
        );
      })}
    </RadioPrimitive.Root>
  );
}
