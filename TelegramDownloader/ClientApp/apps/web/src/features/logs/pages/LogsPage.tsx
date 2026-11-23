import { ScrollText } from 'lucide-react';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function LogsPage() {
  return <PlaceholderPage title={t('Logs')} description={t('Server log viewer.')} icon={<ScrollText />} />;
}
