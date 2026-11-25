import type { ApiChatMessageDto } from '@tfm/api-client';
import { cn, DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger, IconButton } from '@tfm/ui';
import { CheckSquare, CloudDownload, Download, ExternalLink, Eye, MessageSquare, MoreHorizontal, Play } from 'lucide-react';
import { t } from '@/i18n';
import { downloadMessages, saveMessages } from '../chatActions';
import { telegramMessageLink } from '../chatModel';

export interface MessageActionsProps {
  m: ApiChatMessageDto;
  channelId: string;
  onOpen: () => void;
  onSelect?: () => void;
  onGoTo?: () => void;
  className?: string;
}

/** Menu of a message. */
export function MessageActions({ m, channelId, onOpen, onSelect, onGoTo, className }: MessageActionsProps) {
  const link = telegramMessageLink(channelId, m.id);
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <IconButton size="xs" label={t('Message actions')} className={cn('opacity-60 group-hover:opacity-100 focus-visible:opacity-100', className)}>
          <MoreHorizontal />
        </IconButton>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {onGoTo && (
          <DropdownMenuItem icon={<MessageSquare />} onSelect={onGoTo}>
            {t('Go to the message')}
          </DropdownMenuItem>
        )}
        {m.hasMedia && (
          <>
            <DropdownMenuItem icon={m.mediaType === 'audio' ? <Play /> : <Eye />} onSelect={onOpen}>
              {m.mediaType === 'audio' ? t('Play') : t('View')}
            </DropdownMenuItem>
            <DropdownMenuItem icon={<CloudDownload />} onSelect={() => void downloadMessages(channelId, [m.id])}>
              {t('Download to server')}
            </DropdownMenuItem>
            <DropdownMenuItem icon={<Download />} onSelect={() => saveMessages(channelId, [m])}>
              {t('Download to this device')}
            </DropdownMenuItem>
            {onSelect && (
              <DropdownMenuItem icon={<CheckSquare />} onSelect={onSelect}>
                {t('Select')}
              </DropdownMenuItem>
            )}
          </>
        )}
        {link && (
          <DropdownMenuItem asChild icon={<ExternalLink />}>
            <a href={link} target="_blank" rel="noreferrer">
              {t('Open in Telegram')}
            </a>
          </DropdownMenuItem>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
