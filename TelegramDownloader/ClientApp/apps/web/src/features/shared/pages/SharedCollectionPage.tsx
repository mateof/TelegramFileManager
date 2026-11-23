import { Share2 } from 'lucide-react';
import { useParams } from 'react-router';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function SharedCollectionPage() {
  const params = useParams();
  return <PlaceholderPage title={t('Shared collection')} description={t('Browse a shared collection.')} icon={<Share2 />} details={{ 'id': params['id'], '*': params['*'] }} />;
}
