import { cn } from '@tfm/ui';
import type { ReactNode } from 'react';

/** Round glass button of the viewer chrome (always on a dark backdrop). */
export function ViewerButton({ label, onClick, children, className, href, download }: { label: string; onClick?: () => void; children: ReactNode; className?: string; href?: string; download?: string }) {
  const cls = cn(
    'flex size-10 shrink-0 items-center justify-center rounded-full bg-white/10 text-white backdrop-blur-md transition-colors hover:bg-white/20',
    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white disabled:opacity-40 [&_svg]:size-5',
    className
  );
  if (href) {
    return (
      <a href={href} download={download} aria-label={label} title={label} className={cls}>
        {children}
      </a>
    );
  }
  return (
    <button type="button" aria-label={label} title={label} onClick={onClick} className={cls}>
      {children}
    </button>
  );
}

