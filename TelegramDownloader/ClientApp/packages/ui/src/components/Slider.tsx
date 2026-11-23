import { Slider as SliderPrimitive } from 'radix-ui';
import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { cn } from '../lib/cn';

export interface SliderProps extends ComponentPropsWithoutRef<typeof SliderPrimitive.Root> {
  /** One accessible label per thumb. */
  thumbLabels?: string[];
}

export const Slider = forwardRef<HTMLSpanElement, SliderProps>(function Slider(
  { className, thumbLabels, value, defaultValue, ...props },
  ref
) {
  const count = (value ?? defaultValue ?? [0]).length;
  return (
    <SliderPrimitive.Root
      ref={ref}
      value={value}
      defaultValue={defaultValue}
      className={cn('group relative flex h-5 w-full touch-none select-none items-center data-[disabled]:opacity-50', className)}
      {...props}
    >
      <SliderPrimitive.Track className="relative h-1.5 grow overflow-hidden rounded-full bg-surface-3">
        <SliderPrimitive.Range className="absolute h-full rounded-full bg-primary" />
      </SliderPrimitive.Track>
      {Array.from({ length: count }, (_, i) => (
        <SliderPrimitive.Thumb
          key={i}
          aria-label={thumbLabels?.[i]}
          className="block size-4 rounded-full border-2 border-primary bg-white shadow-soft transition-transform duration-150 hover:scale-110 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring active:scale-110"
        />
      ))}
    </SliderPrimitive.Root>
  );
});
