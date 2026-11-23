import { Share2 } from 'lucide-react';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function SharedIndexPage() {
  return <PlaceholderPage title={t('Shared collections')} description={t('Collections other people shared with you.')} icon={<Share2 />} />;
}
