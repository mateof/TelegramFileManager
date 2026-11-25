import { Card, cn } from '@tfm/ui';
import type { ReactNode } from 'react';
import { t } from '@/i18n';
import { Logo } from '@/shell/Logo';

/** Centered glass card used by every pre-app screen (web login, setup, Telegram login). */
export function AuthShell({
  title,
  subtitle,
  children,
  footer,
  wide
}: {
  title: ReactNode;
  subtitle?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}) {
  return (
    <div className="relative flex min-h-dvh items-center justify-center overflow-y-auto px-4 py-10">
      <div aria-hidden className="tfm-ambient" />
      <div className={cn('relative z-10 flex w-full flex-col items-center gap-6', wide ? 'max-w-lg' : 'max-w-sm')}>
        <div className="flex flex-col items-center gap-3 text-center">
          <Logo className="size-14" />
          <div>
            <h1 className="text-2xl font-semibold tracking-tight text-fg">{title}</h1>
            {subtitle && <p className="mt-1 text-sm text-fg-muted">{subtitle}</p>}
          </div>
        </div>
        <Card glass className="w-full p-6 shadow-elevated">
          {children}
        </Card>
        {footer}
        <a href="/" className="text-xs text-fg-subtle transition-colors hover:text-fg-muted">
          {t('Open the classic interface')}
        </a>
      </div>
    </div>
  );
}

/** Full-screen spinner while the session state loads. */
export function SplashScreen() {
  return (
    <div className="flex min-h-dvh items-center justify-center">
      <div aria-hidden className="tfm-ambient" />
      <Logo className="relative size-14 animate-pulse" />
      <span className="sr-only">{t('Loading...')}</span>
    </div>
  );
}
