import { MessagesSquare } from 'lucide-react';
import { useParams } from 'react-router';
import { t } from '@/i18n';
import { PlaceholderPage } from '@/shell/PlaceholderPage';

// PLACEHOLDER: replaced by the feature implementation.
export default function ChatPage() {
  const params = useParams();
  return <PlaceholderPage title={t('Chat')} description={t('Messages and media of this chat.')} icon={<MessagesSquare />} details={{ 'id': params['id'] }} />;
}
