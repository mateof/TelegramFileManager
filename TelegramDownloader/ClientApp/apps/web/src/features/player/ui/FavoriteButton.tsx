import { cn, IconButton } from '@tfm/ui';
import { Heart } from 'lucide-react';
import { t } from '@/i18n';
import type { QueuedTrack } from '../queue';
import type { TrackTags } from '../sync';
import { useFavorite } from './usePlayerSync';

interface FavoriteButtonProps {
  track: QueuedTrack | null;
  tags: TrackTags;
  size?: 'sm';
  className?: string;
}

/** Heart toggle of the current track; hidden when the server has no `player.sync` or the track has no server key. */
export function FavoriteButton({ track, tags, size, className }: FavoriteButtonProps) {
  const { available, isFavorite, toggle } = useFavorite(track, tags);
  if (!available) return null;
  return (
    <IconButton
      label={isFavorite ? t('Remove from favourites') : t('Add to favourites')}
      size={size}
      aria-pressed={isFavorite}
      onClick={toggle}
      className={cn(isFavorite && 'text-accent-400', className)}
    >
      <Heart className={cn(isFavorite && 'fill-current')} />
    </IconButton>
  );
}
