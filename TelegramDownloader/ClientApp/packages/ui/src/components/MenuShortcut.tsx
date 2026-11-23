import type { ReactNode } from 'react';

/** Extra props of menu items. */
export interface MenuItemExtras {
  icon?: ReactNode;
  shortcut?: ReactNode;
  destructive?: boolean;
}

/** Keyboard hint on the right of a menu item. */
export function MenuShortcut({ children }: { children: ReactNode }) {
  return <span className="ml-auto pl-4 text-xs tracking-wide text-fg-subtle">{children}</span>;
}
