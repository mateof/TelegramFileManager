import type { ApiChatMessageDto } from '@tfm/api-client';
import { cn } from '@tfm/ui';
import { Play } from 'lucide-react';
import { useState } from 'react';
import { formatTime } from '@/features/player/format';
import { messageThumbnailUrl } from '../chatActions';
import { mediaIcon } from './mediaIcon';

/** Preview of a photo or video message, with the length of the video. */
export function Thumbnail({ channelId, m, className }: { channelId: string; m: ApiChatMessageDto; className?: string }) {
  const [state, setState] = useState<'loading' | 'ok' | 'error'>('loading');
  return (
    <span className={cn('relative block overflow-hidden bg-surface-3', className)}>
      {state === 'loading' && <span className="absolute inset-0 animate-shimmer bg-gradient-to-r from-surface-3 via-surface-2 to-surface-3 bg-[length:200%_100%]" />}
      {state === 'error' ? (
        <span className="absolute inset-0 flex items-center justify-center text-fg-subtle [&_svg]:size-8">{mediaIcon(m.mediaType)}</span>
      ) : (
        <img
          src={messageThumbnailUrl(channelId, m.id)}
          alt={m.fileName ?? ''}
          loading="lazy"
          decoding="async"
          onLoad={() => setState('ok')}
          onError={() => setState('error')}
          className={cn('size-full object-cover transition-[opacity,filter,transform] duration-500', state === 'ok' ? 'opacity-100 blur-0' : 'opacity-0 blur-md')}
        />
      )}
      {m.mediaType === 'video' && (
        <>
          <span className="absolute inset-0 flex items-center justify-center">
            <span className="flex size-11 items-center justify-center rounded-full bg-black/55 text-white shadow-elevated backdrop-blur-sm [&_svg]:size-5">
              <Play className="translate-x-px" />
            </span>
          </span>
          {!!m.duration && (
            <span className="absolute bottom-1.5 left-1.5 rounded-md bg-black/60 px-1.5 py-0.5 text-[11px] tabular-nums text-white">{formatTime(m.duration)}</span>
          )}
        </>
      )}
    </span>
  );
}
