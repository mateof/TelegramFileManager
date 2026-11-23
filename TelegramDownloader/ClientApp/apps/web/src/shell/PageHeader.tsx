import { cn } from '@tfm/ui';
import { useEffect, type ReactNode } from 'react';

export interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  icon?: ReactNode;
  /** Buttons on the right. */
  actions?: ReactNode;
  className?: string;
  /** Text for document.title (defaults to `title` when it is a string). */
  documentTitle?: string;
}

/** Standard page title block. Also sets the browser tab title. */
export function PageHeader({ title, description, icon, actions, className, documentTitle }: PageHeaderProps) {
  const tabTitle = documentTitle ?? (typeof title === 'string' ? title : undefined);
  useEffect(() => {
    if (tabTitle) document.title = `${tabTitle} · TFM`;
  }, [tabTitle]);

  return (
    <header className={cn('flex flex-wrap items-start justify-between gap-4', className)}>
      <div className="flex min-w-0 items-center gap-3.5">
        {icon && (
          <div className="flex size-11 shrink-0 items-center justify-center rounded-2xl border border-accent-500/25 bg-primary-soft text-accent-300 shadow-soft [&_svg]:size-5">
            {icon}
          </div>
        )}
        <div className="min-w-0">
          <h1 className="truncate text-xl font-semibold tracking-tight text-fg sm:text-2xl">{title}</h1>
          {description && <p className="mt-0.5 text-sm text-fg-muted">{description}</p>}
        </div>
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </header>
  );
}
