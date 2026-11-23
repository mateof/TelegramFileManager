import { HardDrive } from 'lucide-react';
import { useParams } from 'react-router';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function LocalFilesPage() {
  const params = useParams();
  return <PlaceholderPage title={t('Local files')} description={t('Files stored on the server.')} icon={<HardDrive />} details={{ '*': params['*'] }} />;
}
