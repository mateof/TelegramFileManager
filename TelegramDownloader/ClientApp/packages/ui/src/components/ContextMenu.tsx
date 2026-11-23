import { ContextMenu as ContextPrimitive } from 'radix-ui';
import { ChevronRight } from 'lucide-react';
import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { MENU_CONTENT, MENU_DANGER, MENU_ITEM, MENU_LABEL, MENU_SEPARATOR } from '../lib/classes';
import { cn } from '../lib/cn';
import { MenuShortcut, type MenuItemExtras } from './MenuShortcut';

export const ContextMenu = ContextPrimitive.Root;
export const ContextMenuTrigger = ContextPrimitive.Trigger;
export const ContextMenuGroup = ContextPrimitive.Group;
export const ContextMenuSub = ContextPrimitive.Sub;

export const ContextMenuContent = forwardRef<HTMLDivElement, ComponentPropsWithoutRef<typeof ContextPrimitive.Content>>(
  function ContextMenuContent({ className, ...props }, ref) {
    return (
      <ContextPrimitive.Portal>
        <ContextPrimitive.Content ref={ref} className={cn(MENU_CONTENT, className)} {...props} />
      </ContextPrimitive.Portal>
    );
  }
);

export const ContextMenuItem = forwardRef<HTMLDivElement, ComponentPropsWithoutRef<typeof ContextPrimitive.Item> & MenuItemExtras>(
  function ContextMenuItem({ className, icon, shortcut, destructive, children, ...props }, ref) {
    return (
      <ContextPrimitive.Item ref={ref} className={cn(MENU_ITEM, destructive && MENU_DANGER, className)} {...props}>
        {icon}
        <span className="min-w-0 flex-1 truncate">{children}</span>
        {shortcut && <MenuShortcut>{shortcut}</MenuShortcut>}
      </ContextPrimitive.Item>
    );
  }
);

export function ContextMenuLabel({ className, ...props }: ComponentPropsWithoutRef<typeof ContextPrimitive.Label>) {
  return <ContextPrimitive.Label className={cn(MENU_LABEL, className)} {...props} />;
}

export function ContextMenuSeparator({ className, ...props }: ComponentPropsWithoutRef<typeof ContextPrimitive.Separator>) {
  return <ContextPrimitive.Separator className={cn(MENU_SEPARATOR, className)} {...props} />;
}

export function ContextMenuSubTrigger({
  className,
  icon,
  children,
  ...props
}: ComponentPropsWithoutRef<typeof ContextPrimitive.SubTrigger> & { icon?: ReactNode }) {
  return (
    <ContextPrimitive.SubTrigger className={cn(MENU_ITEM, 'data-[state=open]:bg-surface-2', className)} {...props}>
      {icon}
      <span className="flex-1">{children}</span>
      <ChevronRight className="ml-auto" />
    </ContextPrimitive.SubTrigger>
  );
}

export function ContextMenuSubContent({ className, ...props }: ComponentPropsWithoutRef<typeof ContextPrimitive.SubContent>) {
  return (
    <ContextPrimitive.Portal>
      <ContextPrimitive.SubContent className={cn(MENU_CONTENT, className)} {...props} />
    </ContextPrimitive.Portal>
  );
}
