import { FolderOpen } from 'lucide-react';
import { useParams } from 'react-router';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function ChannelPage() {
  const params = useParams();
  return <PlaceholderPage title={t('Channel')} description={t('Browse, upload, download and manage the files of this channel.')} icon={<FolderOpen />} details={{ 'id': params['id'], '*': params['*'] }} />;
}
