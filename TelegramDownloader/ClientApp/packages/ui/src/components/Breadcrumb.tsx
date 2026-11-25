import { ChevronRight } from 'lucide-react';
import { Fragment, type ReactNode } from 'react';
import { cn } from '../lib/cn';
import { uiStrings } from '../lib/strings';

export interface BreadcrumbItem {
  label: ReactNode;
  /** Called when clicked; the last item is the current page and is not clickable. */
  onSelect?: () => void;
  /** Alternatively, render your own link element (router Link). */
  render?: (children: ReactNode, className: string) => ReactNode;
  icon?: ReactNode;
}

export interface BreadcrumbProps {
  items: BreadcrumbItem[];
  className?: string;
  /** Collapse the middle items when there are more than this many. */
  maxItems?: number;
}

export function Breadcrumb({ items, className, maxItems = 5 }: BreadcrumbProps) {
  let shown: (BreadcrumbItem | 'ellipsis')[] = items;
  if (items.length > maxItems && maxItems >= 3) {
    shown = [items[0], 'ellipsis', ...items.slice(items.length - (maxItems - 2))];
  }
  const linkClass =
    'inline-flex max-w-48 items-center gap-1.5 truncate rounded-md px-1.5 py-0.5 text-fg-muted transition-colors hover:bg-surface-2 hover:text-fg [&_svg]:size-4';
  return (
    <nav aria-label={uiStrings.breadcrumb} className={cn('min-w-0', className)}>
      <ol className="flex min-w-0 items-center gap-0.5 text-sm">
        {shown.map((item, i) => {
          const last = i === shown.length - 1;
          return (
            <Fragment key={i}>
              {i > 0 && (
                <li aria-hidden className="flex text-fg-subtle">
                  <ChevronRight className="size-3.5" />
                </li>
              )}
              <li className="flex min-w-0">
                {item === 'ellipsis' ? (
                  <span className="px-1.5 text-fg-subtle">…</span>
                ) : last ? (
                  <span aria-current="page" className="inline-flex max-w-64 items-center gap-1.5 truncate px-1.5 py-0.5 font-medium text-fg [&_svg]:size-4">
                    {item.icon}
                    {item.label}
                  </span>
                ) : item.render ? (
                  item.render(
                    <>
                      {item.icon}
                      {item.label}
                    </>,
                    linkClass
                  )
                ) : (
                  <button type="button" onClick={item.onSelect} className={linkClass}>
                    {item.icon}
                    {item.label}
                  </button>
                )}
              </li>
            </Fragment>
          );
        })}
      </ol>
    </nav>
  );
}
