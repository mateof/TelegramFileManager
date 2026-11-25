import { ChevronLeft, ChevronRight, Download, MessageSquare, X } from 'lucide-react';
import { Dialog as DialogPrimitive } from 'radix-ui';
import { lazy, Suspense, useEffect } from 'react';
import { t } from '@/i18n';
import { formatBytes } from '@/lib/format';
import type { ViewerItem } from '../public';
import { useViewer } from '../viewerStore';
import { ImageView } from './ImageView';
import { DownloadCard, PdfView, TextView } from './DocumentViews';
import { ViewerButton } from './ViewerButton';

const VideoView = lazy(() => import('./VideoView'));

function Body({ item, onPrev, onNext }: { item: ViewerItem; onPrev?: () => void; onNext?: () => void }) {
  switch (item.kind) {
    case 'image':
      return <ImageView key={item.id} item={item} onPrev={onPrev} onNext={onNext} />;
    case 'video':
      return (
        <Suspense fallback={null}>
          <VideoView key={item.id} item={item} />
        </Suspense>
      );
    case 'pdf':
      return <PdfView key={item.id} item={item} />;
    case 'text':
      return <TextView key={item.id} item={item} />;
    default:
      return <DownloadCard key={item.id} item={item} />;
  }
}

/** Full-screen viewer: image gallery, video player, PDF, text, or a download card. Esc closes. */
export default function MediaViewer() {
  const items = useViewer((s) => s.items);
  const index = useViewer((s) => s.index);
  const open = useViewer((s) => s.open);
  const { setIndex, close } = useViewer.getState();
  const item = items[index];
  const count = items.length;
  const onPrev = index > 0 ? () => setIndex(index - 1) : undefined;
  const onNext = index < count - 1 ? () => setIndex(index + 1) : undefined;

  // ←/→ browse (videos keep the arrows for seeking, PageUp/PageDown still browse)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
      if (e.target instanceof Element && e.target.closest('input, textarea, [role="slider"]')) return;
      const video = item?.kind === 'video';
      const s = useViewer.getState();
      if ((e.key === 'ArrowLeft' && !video) || e.key === 'PageUp') {
        if (s.index > 0) s.setIndex(s.index - 1);
      } else if ((e.key === 'ArrowRight' && !video) || e.key === 'PageDown') {
        if (s.index < s.items.length - 1) s.setIndex(s.index + 1);
      } else return;
      e.preventDefault();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [item?.kind]);

  if (!item) return null;
  const download = item.downloadUrl ?? item.url;

  return (
    <DialogPrimitive.Root open={open} onOpenChange={(o) => !o && close()}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-[55] bg-black/90 backdrop-blur-xl data-[state=open]:animate-fade-in" />
        <DialogPrimitive.Content
          aria-describedby={undefined}
          className="fixed inset-0 z-[56] flex flex-col text-white outline-none data-[state=open]:animate-scale-in"
          onPointerDownOutside={(e) => e.preventDefault()}
        >
          <header className="pointer-events-none absolute inset-x-0 top-0 z-10 flex items-center gap-3 bg-gradient-to-b from-black/70 to-transparent px-3 pb-8 pt-[max(0.75rem,env(safe-area-inset-top))] sm:px-5">
            <div className="pointer-events-auto min-w-0 flex-1">
              <DialogPrimitive.Title className="truncate text-sm font-semibold sm:text-base">{item.name}</DialogPrimitive.Title>
              <p className="text-xs text-white/60">
                {count > 1 && t('{index} of {count}', { index: index + 1, count })}
                {count > 1 && item.size ? ' · ' : ''}
                {item.size ? formatBytes(item.size) : ''}
              </p>
            </div>
            <div className="pointer-events-auto flex items-center gap-2">
              {item.goTo && (
                <ViewerButton
                  label={item.goTo.label}
                  onClick={() => {
                    close();
                    item.goTo?.run();
                  }}
                >
                  <MessageSquare />
                </ViewerButton>
              )}
              <ViewerButton label={t('Download')} href={download} download={item.name}>
                <Download />
              </ViewerButton>
              <DialogPrimitive.Close asChild>
                <ViewerButton label={t('Close')}>
                  <X />
                </ViewerButton>
              </DialogPrimitive.Close>
            </div>
          </header>

          <div className="relative flex min-h-0 flex-1 items-center justify-center">
            <Body item={item} onPrev={onPrev} onNext={onNext} />
          </div>

          {count > 1 && (
            <>
              {onPrev && (
                <ViewerButton label={t('Previous')} onClick={onPrev} className="absolute left-3 top-1/2 z-10 -translate-y-1/2 max-sm:hidden">
                  <ChevronLeft />
                </ViewerButton>
              )}
              {onNext && (
                <ViewerButton label={t('Next')} onClick={onNext} className="absolute right-3 top-1/2 z-10 -translate-y-1/2 max-sm:hidden">
                  <ChevronRight />
                </ViewerButton>
              )}
            </>
          )}
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
