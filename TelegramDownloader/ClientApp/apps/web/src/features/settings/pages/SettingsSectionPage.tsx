import { Settings } from 'lucide-react';
import { useParams } from 'react-router';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function SettingsSectionPage() {
  const params = useParams();
  return <PlaceholderPage title={t('Settings')} description={t('Server, Telegram, downloads, appearance and security.')} icon={<Settings />} details={{ 'section': params['section'] }} />;
}
