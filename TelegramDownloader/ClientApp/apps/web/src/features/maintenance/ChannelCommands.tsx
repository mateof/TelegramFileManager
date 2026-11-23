import { LogIn, Plus } from 'lucide-react';
import { useMemo } from 'react';
import { useNavigate } from 'react-router';
import { t } from '@/i18n';
import { useRegisterCommands } from '@/shell/commands';

/**
 * Adds "Create channel" and "Join channel" to the command palette on every page
 * (rendered once by the layout). They open the dialogs of the maintenance page.
 */
export function ChannelCommands() {
  const navigate = useNavigate();
  useRegisterCommands(
    useMemo(
      () => ({
        id: 'channel-management',
        heading: t('Channel management'),
        items: [
          { id: 'create-channel', label: t('Create channel'), keywords: ['new', 'nuevo', 'canal'], icon: <Plus />, onSelect: () => navigate('/maintenance?dialog=create-channel') },
          { id: 'join-channel', label: t('Join channel'), keywords: ['invite', 'link', 'unirse'], icon: <LogIn />, onSelect: () => navigate('/maintenance?dialog=join-channel') }
        ]
      }),
      [navigate]
    )
  );
  return null;
}
