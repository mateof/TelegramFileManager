import { Button, Card, EmptyState } from '@tfm/ui';
import { RefreshCw, TriangleAlert } from 'lucide-react';
import { isRouteErrorResponse, useRouteError } from 'react-router';
import { t } from '@/i18n';

function isChunkLoadError(error: unknown) {
  return error instanceof Error && /dynamically imported module|Importing a module script failed|Failed to fetch/i.test(error.message);
}

/** Error boundary of the routes (also catches lazy chunks that fail to load after a deploy). */
export function RouteError() {
  const error = useRouteError();
  const chunk = isChunkLoadError(error);
  const message = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error
      ? error.message
      : String(error);

  return (
    <div className="flex flex-1 items-center justify-center p-6">
      <Card glass className="w-full max-w-md">
        <EmptyState
          icon={<TriangleAlert />}
          title={chunk ? t('A new version is available') : t('Something went wrong')}
          description={chunk ? t('Reload the page to get the latest version.') : message}
          action={
            <Button icon={<RefreshCw />} onClick={() => window.location.reload()}>
              {t('Reload')}
            </Button>
          }
        />
      </Card>
    </div>
  );
}
