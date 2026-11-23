import { Clapperboard } from 'lucide-react';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function LibraryPage() {
  return <PlaceholderPage title={t('Library')} description={t('Movies and series found in your channels.')} icon={<Clapperboard />} />;
}
