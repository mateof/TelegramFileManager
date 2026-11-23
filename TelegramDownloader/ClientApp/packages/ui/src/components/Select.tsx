import { Select as SelectPrimitive } from 'radix-ui';
import { Check, ChevronDown } from 'lucide-react';
import { type ReactNode } from 'react';
import { FIELD } from '../lib/classes';
import { cn } from '../lib/cn';

export interface SelectOption<V extends string = string> {
  value: V;
  label: ReactNode;
  disabled?: boolean;
}

export interface SelectProps<V extends string = string> {
  value?: V;
  defaultValue?: V;
  onValueChange?: (value: V) => void;
  options: SelectOption<V>[];
  placeholder?: string;
  disabled?: boolean;
  id?: string;
  className?: string;
  'aria-label'?: string;
  size?: 'sm' | 'md';
}

export function Select<V extends string = string>({
  value,
  defaultValue,
  onValueChange,
  options,
  placeholder,
  disabled,
  id,
  className,
  size = 'md',
  ...aria
}: SelectProps<V>) {
  return (
    <SelectPrimitive.Root
      value={value}
      defaultValue={defaultValue}
      onValueChange={(v) => onValueChange?.(v as V)}
      disabled={disabled}
    >
      <SelectPrimitive.Trigger
        id={id}
        className={cn(FIELD, 'flex items-center justify-between gap-2 text-left', size === 'sm' ? 'h-8 px-2.5' : 'h-9.5 px-3', className)}
        {...aria}
      >
        <SelectPrimitive.Value placeholder={placeholder} />
        <SelectPrimitive.Icon>
          <ChevronDown className="size-4 text-fg-subtle" />
        </SelectPrimitive.Icon>
      </SelectPrimitive.Trigger>
      <SelectPrimitive.Portal>
        <SelectPrimitive.Content
          position="popper"
          sideOffset={6}
          className="glass-strong z-50 max-h-(--radix-select-content-available-height) min-w-(--radix-select-trigger-width) overflow-hidden rounded-xl border border-border p-1 shadow-elevated data-[state=open]:animate-pop-in"
        >
          <SelectPrimitive.Viewport>
            {options.map((o) => (
              <SelectPrimitive.Item
                key={o.value}
                value={o.value}
                disabled={o.disabled}
                className="relative flex h-8 cursor-default select-none items-center rounded-md pl-8 pr-3 text-sm text-fg outline-none data-[disabled]:opacity-40 data-[highlighted]:bg-primary-soft data-[highlighted]:text-fg"
              >
                <SelectPrimitive.ItemIndicator className="absolute left-2 flex">
                  <Check className="size-4 text-accent-400" />
                </SelectPrimitive.ItemIndicator>
                <SelectPrimitive.ItemText>{o.label}</SelectPrimitive.ItemText>
              </SelectPrimitive.Item>
            ))}
          </SelectPrimitive.Viewport>
        </SelectPrimitive.Content>
      </SelectPrimitive.Portal>
    </SelectPrimitive.Root>
  );
}
