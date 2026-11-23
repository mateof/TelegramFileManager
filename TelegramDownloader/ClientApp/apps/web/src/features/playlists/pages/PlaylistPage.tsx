import { ListMusic } from 'lucide-react';
import { useParams } from 'react-router';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function PlaylistPage() {
  const params = useParams();
  return <PlaceholderPage title={t('Playlist')} description={t('Tracks of this playlist.')} icon={<ListMusic />} details={{ 'id': params['id'] }} />;
}
