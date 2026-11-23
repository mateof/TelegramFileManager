import { Badge, Card, EmptyState } from '@tfm/ui';
import { Construction } from 'lucide-react';
import type { ReactNode } from 'react';
import { t } from '@/i18n';
import { PageHeader } from './PageHeader';

export interface PlaceholderPageProps {
  title: string;
  description?: string;
  icon?: ReactNode;
  /** Route params or other context, shown to help while developing. */
  details?: Record<string, string | undefined>;
}

/** Temporary page used by features that are not implemented yet. */
export function PlaceholderPage({ title, description, icon, details }: PlaceholderPageProps) {
  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-6 p-4 sm:p-6 lg:p-8">
      <PageHeader title={title} description={description} icon={icon} actions={<Badge tone="warning" dot>{t('Coming soon')}</Badge>} />
      <Card glass>
        <EmptyState
          icon={<Construction />}
          title={t('This section is being built')}
          description={t('Use the classic interface meanwhile.')}
          action={
            <a href="/?ui=classic" className="text-sm font-medium text-accent-400 hover:text-accent-300">
              {t('Open the classic interface')}
            </a>
          }
        />
        {details && Object.keys(details).length > 0 && (
          <dl className="mx-auto mb-8 grid max-w-md grid-cols-[auto_1fr] gap-x-4 gap-y-1 rounded-xl border border-border bg-surface-2/50 px-4 py-3 font-mono text-xs">
            {Object.entries(details).map(([k, v]) => (
              <div key={k} className="contents">
                <dt className="text-fg-subtle">{k}</dt>
                <dd className="truncate text-fg-muted">{v ?? '—'}</dd>
              </div>
            ))}
          </dl>
        )}
      </Card>
    </div>
  );
}
