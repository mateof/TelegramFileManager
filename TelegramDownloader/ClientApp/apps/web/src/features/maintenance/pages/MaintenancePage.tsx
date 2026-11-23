import { Wrench } from 'lucide-react';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function MaintenancePage() {
  return <PlaceholderPage title={t('Maintenance')} description={t('Database tools, import/export and cleanup.')} icon={<Wrench />} />;
}
