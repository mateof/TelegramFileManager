import { Slot } from 'radix-ui';
import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react';
import { cn } from '../lib/cn';
import { Spinner } from './Spinner';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'outline' | 'danger' | 'soft' | 'link';
export type ButtonSize = 'xs' | 'sm' | 'md' | 'lg';

const VARIANTS: Record<ButtonVariant, string> = {
  primary:
    'bg-primary text-on-accent shadow-soft hover:bg-primary-hover active:brightness-95 [background-image:linear-gradient(to_bottom,rgb(255_255_255/0.12),transparent)]',
  secondary: 'bg-surface-2 text-fg border border-border hover:bg-surface-3 shadow-soft',
  ghost: 'text-fg-muted hover:text-fg hover:bg-surface-2',
  outline: 'border border-border-strong/60 text-fg hover:bg-surface-2',
  danger: 'bg-danger text-white shadow-soft hover:brightness-110',
  soft: 'bg-primary-soft text-accent-300 hover:bg-accent-500/25',
  link: 'text-accent-400 hover:text-accent-300 underline-offset-4 hover:underline px-0 h-auto'
};

const SIZES: Record<ButtonSize, string> = {
  xs: 'h-7 px-2.5 text-xs gap-1 rounded-md',
  sm: 'h-8 px-3 text-sm gap-1.5 rounded-lg',
  md: 'h-9.5 px-4 text-sm gap-2 rounded-lg',
  lg: 'h-11 px-5 text-base gap-2 rounded-xl'
};

export function buttonClasses(variant: ButtonVariant = 'primary', size: ButtonSize = 'md', className?: string) {
  return cn(
    'inline-flex shrink-0 select-none items-center justify-center whitespace-nowrap font-medium',
    'transition-[background-color,color,box-shadow,transform,filter] duration-150 active:scale-[0.98]',
    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
    'disabled:pointer-events-none disabled:opacity-50 [&_svg]:size-4 [&_svg]:shrink-0',
    SIZES[size],
    VARIANTS[variant],
    className
  );
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Shows a spinner and disables the button. */
  loading?: boolean;
  /** Icon before the label. */
  icon?: ReactNode;
  /** Icon after the label. */
  iconRight?: ReactNode;
  /** Render the child element instead of a <button> (e.g. a router <Link>). */
  asChild?: boolean;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'primary', size = 'md', loading, icon, iconRight, asChild, className, children, disabled, type, ...props },
  ref
) {
  if (asChild) {
    // Slottable keeps the icons: they are rendered inside the child element (e.g. a <Link>)
    return (
      <Slot.Root ref={ref} className={buttonClasses(variant, size, className)} {...props}>
        {icon}
        <Slot.Slottable>{children}</Slot.Slottable>
        {iconRight}
      </Slot.Root>
    );
  }
  return (
    <button
      ref={ref}
      type={type ?? 'button'}
      className={buttonClasses(variant, size, className)}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...props}
    >
      {loading ? <Spinner size="sm" /> : icon}
      {children}
      {iconRight}
    </button>
  );
});

export interface IconButtonProps extends Omit<ButtonProps, 'icon' | 'iconRight' | 'children'> {
  /** Accessible name (also used as the native tooltip when `title` is not given). */
  label: string;
  children: ReactNode;
}

const ICON_SIZES: Record<ButtonSize, string> = {
  xs: 'size-7 px-0 rounded-md',
  sm: 'size-8 px-0 rounded-lg',
  md: 'size-9.5 px-0 rounded-lg',
  lg: 'size-11 px-0 rounded-xl [&_svg]:size-5'
};

/** Square button with only an icon. `label` is required for screen readers. */
export const IconButton = forwardRef<HTMLButtonElement, IconButtonProps>(function IconButton(
  { label, variant = 'ghost', size = 'md', className, children, title, ...props },
  ref
) {
  return (
    <Button
      ref={ref}
      variant={variant}
      size={size}
      aria-label={label}
      title={title ?? label}
      className={cn(ICON_SIZES[size], className)}
      {...props}
    >
      {children}
    </Button>
  );
});
