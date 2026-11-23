import { cn } from '@tfm/ui';
import { Clapperboard } from 'lucide-react';
import { useState } from 'react';
import { imageAtSize } from '../libraryModel';

export interface LibraryImageProps {
  src: string | null | undefined;
  alt: string;
  /** Size requested from the image cache (`w342`, `w780`, `w1280`...). */
  size: string;
  className?: string;
  imgClassName?: string;
  /** Load immediately (above the fold). */
  eager?: boolean;
}

/**
 * Poster / backdrop / still from `/api/v1/library/images`: lazy loaded, with
 * a blurred tiny version (w92) shown while the real one arrives, and a soft
 * gradient when there is no image at all.
 */
export function LibraryImage(props: LibraryImageProps) {
  // A new source starts from the placeholder again
  return <ImageWithPlaceholder key={props.src ?? ''} {...props} />;
}

function ImageWithPlaceholder({ src, alt, size, className, imgClassName, eager }: LibraryImageProps) {
  const [loaded, setLoaded] = useState(false);
  const [failed, setFailed] = useState(false);
  const full = imageAtSize(src, size);
  const tiny = imageAtSize(src, 'w92');

  return (
    <div className={cn('relative overflow-hidden bg-surface-3', className)}>
      {!full || failed ? (
        <div
          aria-hidden
          className="absolute inset-0 flex items-center justify-center bg-gradient-to-br from-accent-900/50 via-surface-3 to-surface-2 text-fg-subtle [&_svg]:size-8"
        >
          <Clapperboard />
        </div>
      ) : (
        <>
          {tiny && size !== 'w92' && (
            <img
              aria-hidden
              src={tiny}
              alt=""
              loading={eager ? 'eager' : 'lazy'}
              className={cn('absolute inset-0 size-full scale-110 object-cover blur-xl transition-opacity duration-700', loaded && 'opacity-0', imgClassName)}
            />
          )}
          <img
            src={full}
            alt={alt}
            loading={eager ? 'eager' : 'lazy'}
            decoding="async"
            onLoad={() => setLoaded(true)}
            onError={() => setFailed(true)}
            className={cn('absolute inset-0 size-full object-cover transition-opacity duration-700 ease-out-soft', loaded ? 'opacity-100' : 'opacity-0', imgClassName)}
          />
        </>
      )}
    </div>
  );
}
