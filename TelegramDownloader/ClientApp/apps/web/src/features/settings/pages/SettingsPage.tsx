import { Settings } from 'lucide-react';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function SettingsPage() {
  return <PlaceholderPage title={t('Settings')} description={t('Server, Telegram, downloads, appearance and security.')} icon={<Settings />} />;
}
