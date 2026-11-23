import { Activity } from 'lucide-react';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function SystemPage() {
  return <PlaceholderPage title={t('System')} description={t('Server information, resources and live metrics.')} icon={<Activity />} />;
}
