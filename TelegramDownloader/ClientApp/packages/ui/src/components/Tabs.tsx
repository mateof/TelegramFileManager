import { Tabs as TabsPrimitive } from 'radix-ui';
import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { cn } from '../lib/cn';

export const Tabs = TabsPrimitive.Root;

export interface TabsListProps extends ComponentPropsWithoutRef<typeof TabsPrimitive.List> {
  /** `pill` = segmented look, `underline` = classic tabs. */
  variant?: 'pill' | 'underline';
}

export const TabsList = forwardRef<HTMLDivElement, TabsListProps>(function TabsList({ className, variant = 'pill', ...props }, ref) {
  return (
    <TabsPrimitive.List
      ref={ref}
      data-variant={variant}
      className={cn(
        'group/tabs flex items-center',
        variant === 'pill' ? 'gap-0.5 rounded-xl border border-border bg-surface-2/60 p-0.5' : 'gap-4 border-b border-border',
        className
      )}
      {...props}
    />
  );
});

export const TabsTrigger = forwardRef<HTMLButtonElement, ComponentPropsWithoutRef<typeof TabsPrimitive.Trigger>>(function TabsTrigger(
  { className, ...props },
  ref
) {
  return (
    <TabsPrimitive.Trigger
      ref={ref}
      className={cn(
        'inline-flex items-center justify-center gap-1.5 whitespace-nowrap text-sm font-medium text-fg-muted transition-all duration-200 [&_svg]:size-4',
        'hover:text-fg focus-visible:outline-2 focus-visible:outline-ring disabled:opacity-40',
        'group-data-[variant=pill]/tabs:h-8 group-data-[variant=pill]/tabs:rounded-[10px] group-data-[variant=pill]/tabs:px-3',
        'group-data-[variant=pill]/tabs:data-[state=active]:bg-surface group-data-[variant=pill]/tabs:data-[state=active]:text-fg group-data-[variant=pill]/tabs:data-[state=active]:shadow-soft',
        'group-data-[variant=underline]/tabs:-mb-px group-data-[variant=underline]/tabs:h-10 group-data-[variant=underline]/tabs:border-b-2 group-data-[variant=underline]/tabs:border-transparent',
        'group-data-[variant=underline]/tabs:data-[state=active]:border-primary group-data-[variant=underline]/tabs:data-[state=active]:text-fg',
        className
      )}
      {...props}
    />
  );
});

export const TabsContent = forwardRef<HTMLDivElement, ComponentPropsWithoutRef<typeof TabsPrimitive.Content>>(function TabsContent(
  { className, ...props },
  ref
) {
  return (
    <TabsPrimitive.Content
      ref={ref}
      className={cn('outline-none data-[state=active]:animate-fade-in', className)}
      {...props}
    />
  );
});
