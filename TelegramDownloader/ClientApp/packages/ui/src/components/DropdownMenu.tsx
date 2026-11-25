import { DropdownMenu as DropdownPrimitive, Slot } from 'radix-ui';
import { Check, ChevronRight } from 'lucide-react';
import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { MENU_CONTENT, MENU_DANGER, MENU_ITEM, MENU_LABEL, MENU_SEPARATOR, MENU_TEXT } from '../lib/classes';
import { cn } from '../lib/cn';
import { MenuShortcut, type MenuItemExtras } from './MenuShortcut';

export const DropdownMenu = DropdownPrimitive.Root;
export const DropdownMenuTrigger = DropdownPrimitive.Trigger;
export const DropdownMenuGroup = DropdownPrimitive.Group;
export const DropdownMenuSub = DropdownPrimitive.Sub;
export const DropdownMenuRadioGroup = DropdownPrimitive.RadioGroup;

export const DropdownMenuContent = forwardRef<HTMLDivElement, ComponentPropsWithoutRef<typeof DropdownPrimitive.Content>>(
  function DropdownMenuContent({ className, sideOffset = 6, collisionPadding = 8, ...props }, ref) {
    return (
      <DropdownPrimitive.Portal>
        <DropdownPrimitive.Content ref={ref} sideOffset={sideOffset} collisionPadding={collisionPadding} className={cn(MENU_CONTENT, className)} {...props} />
      </DropdownPrimitive.Portal>
    );
  }
);

export const DropdownMenuItem = forwardRef<HTMLDivElement, ComponentPropsWithoutRef<typeof DropdownPrimitive.Item> & MenuItemExtras>(
  function DropdownMenuItem({ className, icon, shortcut, destructive, asChild, children, ...props }, ref) {
    return (
      <DropdownPrimitive.Item ref={ref} asChild={asChild} className={cn(MENU_ITEM, destructive && MENU_DANGER, className)} {...props}>
        {icon}
        {/* Slottable: the child (a link) becomes the item itself, with the icon and the shortcut inside */}
        {asChild ? <Slot.Slottable>{children}</Slot.Slottable> : <span className={MENU_TEXT}>{children}</span>}
        {shortcut && <MenuShortcut>{shortcut}</MenuShortcut>}
      </DropdownPrimitive.Item>
    );
  }
);

export const DropdownMenuCheckboxItem = forwardRef<HTMLDivElement, ComponentPropsWithoutRef<typeof DropdownPrimitive.CheckboxItem>>(
  function DropdownMenuCheckboxItem({ className, children, ...props }, ref) {
    return (
      <DropdownPrimitive.CheckboxItem ref={ref} className={cn(MENU_ITEM, 'pl-8', className)} {...props}>
        <DropdownPrimitive.ItemIndicator className="absolute left-2.5 flex">
          <Check className="text-accent-400!" />
        </DropdownPrimitive.ItemIndicator>
        {children}
      </DropdownPrimitive.CheckboxItem>
    );
  }
);

export const DropdownMenuRadioItem = forwardRef<HTMLDivElement, ComponentPropsWithoutRef<typeof DropdownPrimitive.RadioItem>>(
  function DropdownMenuRadioItem({ className, children, ...props }, ref) {
    return (
      <DropdownPrimitive.RadioItem ref={ref} className={cn(MENU_ITEM, 'pl-8', className)} {...props}>
        <DropdownPrimitive.ItemIndicator className="absolute left-3 flex">
          <span className="size-2 rounded-full bg-primary" />
        </DropdownPrimitive.ItemIndicator>
        {children}
      </DropdownPrimitive.RadioItem>
    );
  }
);

export function DropdownMenuLabel({ className, ...props }: ComponentPropsWithoutRef<typeof DropdownPrimitive.Label>) {
  return <DropdownPrimitive.Label className={cn(MENU_LABEL, className)} {...props} />;
}

export function DropdownMenuSeparator({ className, ...props }: ComponentPropsWithoutRef<typeof DropdownPrimitive.Separator>) {
  return <DropdownPrimitive.Separator className={cn(MENU_SEPARATOR, className)} {...props} />;
}

export function DropdownMenuSubTrigger({
  className,
  icon,
  children,
  ...props
}: ComponentPropsWithoutRef<typeof DropdownPrimitive.SubTrigger> & { icon?: ReactNode }) {
  return (
    <DropdownPrimitive.SubTrigger className={cn(MENU_ITEM, 'data-[state=open]:bg-surface-2', className)} {...props}>
      {icon}
      <span className="flex-1">{children}</span>
      <ChevronRight className="ml-auto" />
    </DropdownPrimitive.SubTrigger>
  );
}

export function DropdownMenuSubContent({ className, ...props }: ComponentPropsWithoutRef<typeof DropdownPrimitive.SubContent>) {
  return (
    <DropdownPrimitive.Portal>
      <DropdownPrimitive.SubContent className={cn(MENU_CONTENT, className)} {...props} />
    </DropdownPrimitive.Portal>
  );
}
