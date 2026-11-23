import { ToggleGroup as ToggleGroupPrimitive } from 'radix-ui';
import { type ReactNode } from 'react';
import { cn } from '../lib/cn';
import type { SelectOption } from './Select';

export interface SegmentedControlProps<V extends string = string> {
  value: V;
  onValueChange: (value: V) => void;
  options: (SelectOption<V> & { icon?: ReactNode })[];
  size?: 'sm' | 'md';
  className?: string;
  'aria-label'?: string;
  /** Stretch the segments to the full width. */
  fullWidth?: boolean;
}

/** iOS-style single choice between a few options (view modes, filters). */
export function SegmentedControl<V extends string = string>({
  value,
  onValueChange,
  options,
  size = 'md',
  className,
  fullWidth,
  ...aria
}: SegmentedControlProps<V>) {
  return (
    <ToggleGroupPrimitive.Root
      type="single"
      value={value}
      onValueChange={(v) => {
        if (v) onValueChange(v as V);
      }}
      className={cn('inline-flex items-center gap-0.5 rounded-xl border border-border bg-surface-2/60 p-0.5', fullWidth && 'flex w-full', className)}
      {...aria}
    >
      {options.map((o) => (
        <ToggleGroupPrimitive.Item
          key={o.value}
          value={o.value}
          disabled={o.disabled}
          className={cn(
            'inline-flex items-center justify-center gap-1.5 rounded-[10px] font-medium text-fg-muted transition-all duration-200 [&_svg]:size-4',
            'hover:text-fg focus-visible:outline-2 focus-visible:outline-ring data-[state=on]:bg-surface data-[state=on]:text-fg data-[state=on]:shadow-soft disabled:opacity-40',
            size === 'sm' ? 'h-7 px-2.5 text-xs' : 'h-8 px-3 text-sm',
            fullWidth && 'flex-1'
          )}
        >
          {o.icon}
          {o.label}
        </ToggleGroupPrimitive.Item>
      ))}
    </ToggleGroupPrimitive.Root>
  );
}
