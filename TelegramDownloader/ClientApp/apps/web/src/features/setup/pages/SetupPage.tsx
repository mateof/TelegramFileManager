import { Rocket } from 'lucide-react';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function SetupPage() {
  return <PlaceholderPage title={t('First-run setup')} description={t('Configure the Telegram API keys and the database.')} icon={<Rocket />} />;
}
