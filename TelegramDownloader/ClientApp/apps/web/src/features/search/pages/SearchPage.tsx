import { Search } from 'lucide-react';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function SearchPage() {
  return <PlaceholderPage title={t('Search')} description={t('Search files in every channel.')} icon={<Search />} />;
}
