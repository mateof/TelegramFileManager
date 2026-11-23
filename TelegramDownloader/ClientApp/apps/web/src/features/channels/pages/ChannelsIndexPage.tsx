import { FolderTree } from 'lucide-react';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function ChannelsIndexPage() {
  return <PlaceholderPage title={t('Channels')} description={t('Pick a channel in the sidebar to browse its files.')} icon={<FolderTree />} />;
}
