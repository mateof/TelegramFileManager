import { cn } from '@tfm/ui';
import { Archive, File, FileText, Film, Image, Music, Package } from 'lucide-react';

const STYLES: Record<string, { icon: typeof File; className: string }> = {
  Audio: { icon: Music, className: 'text-accent-300 bg-primary-soft' },
  Video: { icon: Film, className: 'text-info bg-info-soft' },
  Photo: { icon: Image, className: 'text-success bg-success-soft' },
  Document: { icon: FileText, className: 'text-warning bg-warning-soft' },
  Archive: { icon: Archive, className: 'text-fg-muted bg-surface-3' },
  Application: { icon: Package, className: 'text-fg-muted bg-surface-3' }
};

/** Coloured tile with the icon of a file category (Audio, Video, Photo...). */
export function FileIcon({ category, className }: { category: string; className?: string }) {
  const style = STYLES[category] ?? { icon: File, className: 'text-fg-muted bg-surface-2' };
  const Icon = style.icon;
  return (
    <span aria-hidden className={cn('flex size-9 shrink-0 items-center justify-center rounded-lg [&_svg]:size-4.5', style.className, className)}>
      <Icon />
    </span>
  );
}
