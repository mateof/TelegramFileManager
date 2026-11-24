import { cn } from '@tfm/ui';
import { Clapperboard } from 'lucide-react';
import { useEffect, useState } from 'react';
import { imageAtSize, imageSrcSet } from '../libraryModel';

/** Wait before asking the server again for an image that failed (it remembers provider failures briefly). */
export const IMAGE_RETRY_MS = 4000;

export interface LibraryImageProps {
  src: string | null | undefined;
  alt: string;
  /** Size requested from the image cache (`w342`, `w780`, `w1280`...). */
  size: string;
  /** Size for high-density screens (`srcset` 2x), when the 1x size would look soft. */
  size2x?: string;
  className?: string;
  imgClassName?: string;
  /** Load immediately (above the fold). */
  eager?: boolean;
}

/**
 * Poster / backdrop / still from `/api/v1/library/images` (cached on the
 * server, long-lived in the browser): lazy loaded and decoded off the main
 * thread, with a blurred tiny version (w92) shown while the real one arrives,
 * one retry when it fails, and a soft gradient when there is no image at all.
 */
export function LibraryImage(props: LibraryImageProps) {
  // A new source starts from the placeholder again
  return <ImageWithPlaceholder key={props.src ?? ''} {...props} />;
}

function ImageWithPlaceholder({ src, alt, size, size2x, className, imgClassName, eager }: LibraryImageProps) {
  const [loaded, setLoaded] = useState(false);
  // 0: first try, 1: failed once (retry pending), 2: retried
  const [attempt, setAttempt] = useState(0);
  const [failed, setFailed] = useState(false);
  const [tinyFailed, setTinyFailed] = useState(false);
  const full = imageAtSize(src, size);
  const srcSet = size2x ? imageSrcSet(src, size, size2x) : undefined;
  const tiny = imageAtSize(src, 'w92');

  // Errors are answered with no-store, so a new <img> really asks the server again
  useEffect(() => {
    if (attempt !== 1 || !failed) return;
    const timer = setTimeout(() => {
      setFailed(false);
      setAttempt(2);
    }, IMAGE_RETRY_MS);
    return () => clearTimeout(timer);
  }, [attempt, failed]);

  const gaveUp = failed && attempt >= 2;

  return (
    <div className={cn('relative overflow-hidden bg-surface-3', className)}>
      {!full || gaveUp || (failed && tinyFailed) ? (
        <div
          aria-hidden
          data-testid="library-image-placeholder"
          className="absolute inset-0 flex items-center justify-center bg-gradient-to-br from-accent-900/50 via-surface-3 to-surface-2 text-fg-subtle [&_svg]:size-8"
        >
          <Clapperboard />
        </div>
      ) : (
        <>
          {tiny && size !== 'w92' && !tinyFailed && (
            <img
              aria-hidden
              src={tiny}
              alt=""
              loading={eager ? 'eager' : 'lazy'}
              decoding="async"
              onError={() => setTinyFailed(true)}
              className={cn('absolute inset-0 size-full scale-110 object-cover blur-xl transition-opacity duration-700', loaded && 'opacity-0', imgClassName)}
            />
          )}
          {!failed && (
            <img
              key={attempt}
              src={full}
              srcSet={srcSet}
              alt={alt}
              loading={eager ? 'eager' : 'lazy'}
              decoding="async"
              onLoad={() => setLoaded(true)}
              onError={() => {
                setFailed(true);
                setAttempt((a) => Math.max(a, 1));
              }}
              className={cn('absolute inset-0 size-full object-cover transition-opacity duration-700 ease-out-soft', loaded ? 'opacity-100' : 'opacity-0', imgClassName)}
            />
          )}
        </>
      )}
    </div>
  );
}
