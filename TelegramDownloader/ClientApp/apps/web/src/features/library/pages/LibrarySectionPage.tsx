import { Clapperboard } from 'lucide-react';
import { useParams } from 'react-router';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function LibrarySectionPage() {
  const params = useParams();
  return <PlaceholderPage title={t('Library')} description={t('Movies and series found in your channels.')} icon={<Clapperboard />} details={{ 'section': params['section'] }} />;
}
