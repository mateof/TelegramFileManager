import { Command } from 'cmdk';
import { Dialog as DialogPrimitive } from 'radix-ui';
import { Search } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { cn } from '../lib/cn';
import { uiStrings } from '../lib/strings';
import { Kbd } from './Kbd';

export interface CommandItem {
  id: string;
  label: string;
  /** Extra words that should match the search (aliases, ids...). */
  keywords?: string[];
  icon?: ReactNode;
  /** Secondary text on the right (shortcut, type, channel name...). */
  hint?: ReactNode;
  onSelect: () => void;
}

export interface CommandGroup {
  id: string;
  heading: string;
  items: CommandItem[];
}

export interface CommandPaletteProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  groups: CommandGroup[];
  placeholder?: string;
  /** Controlled search text (e.g. to fetch remote results). */
  search?: string;
  onSearchChange?: (value: string) => void;
  /** Shown under the list (e.g. "Enter to search files"). */
  footer?: ReactNode;
  emptyText?: ReactNode;
  /** Extra content rendered inside the list after the groups (remote results, spinner). */
  children?: ReactNode;
}

/**
 * Ctrl/Cmd+K palette (cmdk). The app wires the keyboard shortcut and feeds
 * the groups (pages, channels, actions); selecting an item closes the palette.
 */
export function CommandPalette({
  open,
  onOpenChange,
  groups,
  placeholder,
  search,
  onSearchChange,
  footer,
  emptyText,
  children
}: CommandPaletteProps) {
  const [innerSearch, setInnerSearch] = useState('');
  const value = search ?? innerSearch;
  const setValue = onSearchChange ?? setInnerSearch;

  return (
    <DialogPrimitive.Root
      open={open}
      onOpenChange={(o) => {
        onOpenChange(o);
        if (!o) setValue('');
      }}
    >
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/45 backdrop-blur-[2px] data-[state=open]:animate-fade-in data-[state=closed]:animate-fade-out" />
        <DialogPrimitive.Content
          aria-describedby={undefined}
          className="glass-strong fixed left-1/2 top-[12vh] z-50 w-[calc(100vw-1.5rem)] max-w-xl -translate-x-1/2 overflow-hidden rounded-2xl border border-border shadow-elevated outline-none data-[state=open]:animate-scale-in data-[state=closed]:animate-scale-out"
        >
          <DialogPrimitive.Title className="sr-only">{placeholder ?? uiStrings.searchPlaceholder}</DialogPrimitive.Title>
          <Command loop className="flex flex-col" label={placeholder ?? uiStrings.searchPlaceholder}>
            <div className="flex items-center gap-3 border-b border-border px-4">
              <Search className="size-4.5 shrink-0 text-fg-subtle" />
              <Command.Input
                value={value}
                onValueChange={setValue}
                placeholder={placeholder ?? uiStrings.searchPlaceholder}
                className="h-13 w-full bg-transparent text-[15px] text-fg outline-none placeholder:text-fg-subtle"
              />
              <Kbd>Esc</Kbd>
            </div>
            <Command.List className="max-h-[min(60vh,26rem)] overflow-y-auto overscroll-contain p-2 [&_[cmdk-list-sizer]]:flex [&_[cmdk-list-sizer]]:flex-col [&_[cmdk-list-sizer]]:gap-1">
              <Command.Empty className="py-10 text-center text-sm text-fg-muted">{emptyText ?? uiStrings.noResults}</Command.Empty>
              {groups
                .filter((g) => g.items.length > 0)
                .map((group) => (
                  <Command.Group
                    key={group.id}
                    heading={group.heading}
                    className="[&_[cmdk-group-heading]]:px-2.5 [&_[cmdk-group-heading]]:pb-1 [&_[cmdk-group-heading]]:pt-2 [&_[cmdk-group-heading]]:text-[11px] [&_[cmdk-group-heading]]:font-semibold [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:tracking-wider [&_[cmdk-group-heading]]:text-fg-subtle"
                  >
                    {group.items.map((item) => (
                      <Command.Item
                        key={item.id}
                        value={`${group.id}:${item.id}:${item.label}`}
                        keywords={[item.label, ...(item.keywords ?? [])]}
                        onSelect={() => {
                          onOpenChange(false);
                          setValue('');
                          item.onSelect();
                        }}
                        className={cn(
                          'flex h-10 cursor-default select-none items-center gap-3 rounded-lg px-2.5 text-sm text-fg outline-none',
                          'data-[selected=true]:bg-primary-soft [&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:text-fg-muted data-[selected=true]:[&_svg]:text-accent-300'
                        )}
                      >
                        {item.icon}
                        <span className="min-w-0 flex-1 truncate">{item.label}</span>
                        {item.hint && <span className="shrink-0 text-xs text-fg-subtle">{item.hint}</span>}
                      </Command.Item>
                    ))}
                  </Command.Group>
                ))}
              {children}
            </Command.List>
            {footer && <div className="flex items-center gap-3 border-t border-border px-4 py-2.5 text-xs text-fg-muted">{footer}</div>}
          </Command>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}

export { Command };
