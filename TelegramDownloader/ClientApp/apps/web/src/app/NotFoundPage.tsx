import { Button, EmptyState } from '@tfm/ui';
import { Compass } from 'lucide-react';
import { Link } from 'react-router';
import { t } from '@/i18n';

export default function NotFoundPage() {
  return (
    <EmptyState
      className="flex-1"
      icon={<Compass />}
      title={t('Page not found')}
      description={t('The address does not match any page of the app.')}
      action={
        <Button asChild variant="secondary">
          <Link to="/channels">{t('Go to channels')}</Link>
        </Button>
      }
    />
  );
}
