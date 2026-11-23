// Class names shared by several components of @tfm/ui

/** Text-like form controls (Input, Textarea, Select trigger). */
export const FIELD =
  'w-full rounded-lg border border-border bg-surface-2/60 text-sm text-fg placeholder:text-fg-subtle shadow-[inset_0_1px_0_rgb(0_0_0/0.04)] ' +
  'transition-[border-color,box-shadow,background-color] duration-150 hover:border-border-strong/70 ' +
  'focus:border-accent-500/70 focus:bg-surface-2 focus:outline-none focus:ring-3 focus:ring-accent-500/20 ' +
  'disabled:cursor-not-allowed disabled:opacity-50 aria-[invalid=true]:border-danger/70 aria-[invalid=true]:focus:ring-danger/20';

/** Dimmed backdrop of modal overlays. */
export const OVERLAY =
  'fixed inset-0 z-50 bg-black/50 backdrop-blur-[2px] data-[state=open]:animate-fade-in data-[state=closed]:animate-fade-out';

/** Menus (dropdown, context). */
export const MENU_CONTENT =
  'glass-strong z-50 min-w-48 overflow-hidden rounded-xl border border-border p-1 text-fg shadow-elevated outline-none ' +
  'data-[state=open]:animate-pop-in data-[state=closed]:animate-fade-out';
export const MENU_ITEM =
  'relative flex h-8 cursor-default select-none items-center gap-2.5 rounded-md px-2.5 text-sm outline-none transition-colors ' +
  'data-[disabled]:pointer-events-none data-[disabled]:opacity-40 data-[highlighted]:bg-primary-soft data-[highlighted]:text-fg ' +
  '[&_svg]:size-4 [&_svg]:shrink-0 [&_svg]:text-fg-muted data-[highlighted]:[&_svg]:text-accent-300';
export const MENU_DANGER =
  'text-danger data-[highlighted]:bg-danger-soft data-[highlighted]:text-danger [&_svg]:text-danger data-[highlighted]:[&_svg]:text-danger';
export const MENU_LABEL = 'px-2.5 pb-1 pt-2 text-[11px] font-semibold uppercase tracking-wider text-fg-subtle';
export const MENU_SEPARATOR = '-mx-1 my-1 h-px bg-border';
