import type { ReleaseDto } from '@tfm/api-client';
import { Badge, Button, Card, Skeleton } from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowUpRight, PartyPopper, RefreshCw, Tag } from 'lucide-react';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';

const versionKey = ['system', 'version'] as const;

function ReleaseLine({ release, label, isNew }: { release: ReleaseDto; label: string; isNew: boolean }) {
  return (
    <div className="flex flex-wrap items-center gap-2 text-sm">
      <span className="text-fg-muted">{label}</span>
      <span className="font-medium text-fg">{release.version}</span>
      {release.publishedAt && <span className="text-xs text-fg-subtle">{new Date(release.publishedAt).toLocaleDateString(getLanguage())}</span>}
      {isNew && (
        <Badge tone="success" dot>
          {t('New')}
        </Badge>
      )}
      {release.url && (
        <a href={release.url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-0.5 text-xs text-accent-400 hover:text-accent-300">
          {t('Release notes')} <ArrowUpRight className="size-3" />
        </a>
      )}
    </div>
  );
}

/** Current version and the GitHub update check (`GET /api/v1/system/version`). */
export function VersionCard({ className }: { className?: string }) {
  const qc = useQueryClient();
  const version = useQuery({ queryKey: versionKey, queryFn: ({ signal }) => api.system.version(false, signal), staleTime: 30 * 60_000 });
  const check = useMutation({ mutationFn: () => api.system.version(true), onSuccess: (data) => qc.setQueryData(versionKey, data) });
  const v = version.data;
  const hasUpdate = !!v && (v.hasNewRelease || (v.isDevVersion && v.hasNewDev));

  return (
    <Card className={className}>
      <div className="flex flex-wrap items-start justify-between gap-3 p-5">
        <div className="flex items-center gap-3">
          <span className="flex size-11 items-center justify-center rounded-2xl bg-primary-soft text-accent-300">
            {hasUpdate ? <PartyPopper className="size-5" /> : <Tag className="size-5" />}
          </span>
          <div>
            <p className="text-xs uppercase tracking-wide text-fg-subtle">{t('Installed version')}</p>
            {version.isPending ? (
              <Skeleton className="mt-1 h-6 w-28" />
            ) : (
              <p className="flex items-center gap-2 text-lg font-semibold text-fg">
                {v?.currentVersion ?? '–'}
                {v?.isDevVersion && <Badge tone="warning">dev</Badge>}
              </p>
            )}
          </div>
        </div>
        <Button variant="secondary" size="sm" icon={<RefreshCw />} loading={check.isPending} onClick={() => check.mutate()}>
          {t('Check for updates')}
        </Button>
      </div>
      <div className="flex flex-col gap-2 border-t border-border px-5 py-4">
        {version.isError || check.isError ? (
          <p className="text-sm text-danger">{errorMessage(check.error ?? version.error)}</p>
        ) : v ? (
          <>
            {hasUpdate ? (
              <p className="text-sm font-medium text-success">{t('An update is available.')}</p>
            ) : (
              <p className="text-sm text-fg-muted">{v.latestRelease || v.latestDev ? t('You are on the latest version.') : t('GitHub could not be reached.')}</p>
            )}
            {v.latestRelease && <ReleaseLine release={v.latestRelease} label={t('Latest release')} isNew={v.hasNewRelease} />}
            {v.isDevVersion && v.latestDev && <ReleaseLine release={v.latestDev} label={t('Latest dev build')} isNew={v.hasNewDev} />}
          </>
        ) : (
          <Skeleton className="h-5 w-64" />
        )}
      </div>
    </Card>
  );
}
