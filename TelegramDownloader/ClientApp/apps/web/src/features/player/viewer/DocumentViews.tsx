import { Button, Spinner } from '@tfm/ui';
import { useQuery } from '@tanstack/react-query';
import { Download, FileQuestion, FileText } from 'lucide-react';
import { t } from '@/i18n';
import { formatBytes } from '@/lib/format';
import type { ViewerItem } from '../public';

/** Text files above this size are not previewed (offered as a download). */
export const TEXT_PREVIEW_MAX_BYTES = 2 * 1024 * 1024;

/** PDF in the browser's own viewer. */
export function PdfView({ item }: { item: ViewerItem }) {
  return (
    <div className="flex size-full flex-col px-2 pb-2 pt-20 sm:px-6 sm:pb-6">
      <iframe title={item.name} src={item.url} className="min-h-0 w-full flex-1 rounded-2xl border border-white/10 bg-white shadow-elevated" />
    </div>
  );
}

/** Small text files, shown as plain text. */
export function TextView({ item }: { item: ViewerItem }) {
  const tooBig = (item.size ?? 0) > TEXT_PREVIEW_MAX_BYTES;
  const { data, isLoading, isError } = useQuery({
    queryKey: ['player', 'text-preview', item.url],
    enabled: !tooBig,
    staleTime: 60_000,
    queryFn: async ({ signal }) => {
      const res = await fetch(item.url, { signal, credentials: 'same-origin', headers: { Range: `bytes=0-${TEXT_PREVIEW_MAX_BYTES - 1}` } });
      if (!res.ok) throw new Error(String(res.status));
      return res.text();
    }
  });
  if (tooBig || isError) return <DownloadCard item={item} reason={tooBig ? t('This file is too large to preview.') : t('The file could not be loaded')} />;
  return (
    <div className="flex size-full flex-col px-2 pb-2 pt-20 sm:px-6 sm:pb-6">
      <div className="min-h-0 w-full flex-1 overflow-auto rounded-2xl border border-white/10 bg-neutral-950/80 shadow-elevated" data-no-player-keys>
        {isLoading ? (
          <div className="flex h-full items-center justify-center">
            <Spinner size="lg" className="text-white" />
          </div>
        ) : (
          <pre className="whitespace-pre-wrap break-words p-4 font-mono text-[13px] leading-relaxed text-neutral-100 sm:p-6">{data}</pre>
        )}
      </div>
    </div>
  );
}

/** Anything the browser cannot show. */
export function DownloadCard({ item, reason }: { item: ViewerItem; reason?: string }) {
  const Icon = item.kind === 'text' ? FileText : FileQuestion;
  return (
    <div className="mx-4 flex w-full max-w-sm flex-col items-center gap-4 rounded-3xl border border-white/10 bg-white/5 p-8 text-center shadow-elevated backdrop-blur-xl">
      <div className="flex size-16 items-center justify-center rounded-2xl bg-white/10">
        <Icon className="size-8 text-white/80" />
      </div>
      <div className="min-w-0">
        <p className="break-words font-semibold">{item.name}</p>
        <p className="mt-1 text-sm text-white/60">
          {[item.mimeType, item.size ? formatBytes(item.size) : null].filter(Boolean).join(' · ')}
        </p>
      </div>
      <p className="text-sm text-white/70">{reason ?? t('There is no preview for this kind of file.')}</p>
      <Button asChild size="lg">
        <a href={item.downloadUrl ?? item.url} download={item.name}>
          <Download />
          {t('Download')}
        </a>
      </Button>
    </div>
  );
}
