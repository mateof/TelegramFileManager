import { cn } from '@tfm/ui';
import { Music } from 'lucide-react';
import { useState } from 'react';
import { fallbackGradient } from '../metadata';

export interface ArtworkProps {
  src?: string | null;
  seed: string;
  className?: string;
  iconClassName?: string;
}

/** Cover art, or a gradient (stable per track) with a note when there is none or it fails to load. */
export function Artwork({ src, seed, className, iconClassName }: ArtworkProps) {
  const [failed, setFailed] = useState<string | null>(null);
  const show = src && failed !== src;
  return (
    <div
      className={cn('relative shrink-0 overflow-hidden bg-surface-2 shadow-soft', className)}
      style={show ? undefined : { backgroundImage: fallbackGradient(seed) }}
    >
      {show ? (
        <img src={src} alt="" draggable={false} className="size-full object-cover" onError={() => setFailed(src)} />
      ) : (
        <div className="flex size-full items-center justify-center text-white/80">
          <Music className={cn('size-1/3', iconClassName)} />
        </div>
      )}
    </div>
  );
}
