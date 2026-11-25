import { Avatar as AvatarPrimitive } from 'radix-ui';
import { cn } from '../lib/cn';

export interface AvatarProps {
  src?: string | null;
  /** Used for the alt text and the initials fallback. */
  name: string;
  size?: 'xs' | 'sm' | 'md' | 'lg';
  className?: string;
  /** Rounded square instead of a circle (channels). */
  square?: boolean;
}

const AVATAR_SIZES = { xs: 'size-6 text-[10px]', sm: 'size-8 text-xs', md: 'size-10 text-sm', lg: 'size-14 text-lg' } as const;

function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  const first = [...parts[0]][0] ?? '';
  const second = parts.length > 1 ? ([...parts[parts.length - 1]][0] ?? '') : '';
  return (first + second).toUpperCase();
}

/** Stable hue from a string, so a channel always gets the same fallback colour. */
function hueOf(name: string): number {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) % 360;
  return h;
}

export function Avatar({ src, name, size = 'md', className, square }: AvatarProps) {
  const hue = hueOf(name);
  return (
    <AvatarPrimitive.Root
      className={cn(
        'relative inline-flex shrink-0 select-none items-center justify-center overflow-hidden',
        square ? 'rounded-xl' : 'rounded-full',
        AVATAR_SIZES[size],
        className
      )}
    >
      {src && <AvatarPrimitive.Image src={src} alt={name} className="size-full object-cover" loading="lazy" />}
      <AvatarPrimitive.Fallback
        delayMs={src ? 400 : 0}
        className="flex size-full items-center justify-center font-semibold text-white"
        style={{ background: `linear-gradient(135deg, hsl(${hue} 60% 50%), hsl(${(hue + 40) % 360} 65% 38%))` }}
      >
        {initials(name)}
      </AvatarPrimitive.Fallback>
    </AvatarPrimitive.Root>
  );
}
