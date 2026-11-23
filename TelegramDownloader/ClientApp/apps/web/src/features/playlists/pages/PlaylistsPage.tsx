import { ListMusic } from 'lucide-react';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function PlaylistsPage() {
  return <PlaceholderPage title={t('Playlists')} description={t('Your audio playlists.')} icon={<ListMusic />} />;
}
