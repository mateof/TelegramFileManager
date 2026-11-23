import { cn, Spinner } from '@tfm/ui';
import { Suspense } from 'react';
import { Outlet, useNavigation } from 'react-router';
import { ChannelCommands } from '@/features/maintenance/ChannelCommands';
import { PlayerSlot } from '@/features/player/PlayerSlot';
import { t } from '@/i18n';
import { useUi } from '@/stores/ui';
import { CommandPaletteHost } from './CommandPaletteHost';
import { BottomNav, MobileDrawer } from './MobileNav';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';

function RouteFallback() {
  return (
    <div className="flex flex-1 items-center justify-center py-24 text-fg-muted">
      <Spinner size="lg" />
    </div>
  );
}

/**
 * Signed-in layout:
 *
 * ```
 * ┌──────────┬──────────────────────────────┐
 * │          │ TopBar                       │
 * │ Sidebar  ├──────────────────────────────┤
 * │ (lg+)    │ <Outlet/> (scrolls)          │
 * │          ├──────────────────────────────┤
 * │          │ PlayerSlot (global player)   │
 * └──────────┴──────────────────────────────┘
 *              BottomNav (< lg)
 * ```
 *
 * The content area is the scroll container (`#tfm-main`): pages that need
 * their own full-height layout (file manager) can use `h-full`.
 */
export function AppLayout() {
  const collapsed = useUi((s) => s.sidebarCollapsed);
  const navigation = useNavigation();
  const loading = navigation.state === 'loading';

  return (
    <div className="relative flex h-dvh overflow-hidden">
      <div aria-hidden className="tfm-ambient" />
      <a
        href="#tfm-main"
        className="sr-only z-50 rounded-lg bg-primary px-3 py-2 text-on-accent focus:not-sr-only focus:fixed focus:left-3 focus:top-3"
      >
        {t('Skip to content')}
      </a>

      <aside
        className={cn(
          'glass relative z-20 hidden shrink-0 border-r border-border transition-[width] duration-300 ease-out-soft lg:block',
          collapsed ? 'w-16' : 'w-72'
        )}
      >
        <Sidebar collapsed={collapsed} />
      </aside>

      <div className="relative z-10 flex min-w-0 flex-1 flex-col">
        <TopBar />
        <div
          aria-hidden
          className={cn(
            'pointer-events-none absolute inset-x-0 top-14 z-40 h-0.5 origin-left bg-primary transition-[opacity,transform] duration-500',
            loading ? 'scale-x-75 opacity-100' : 'scale-x-0 opacity-0'
          )}
        />
        <main id="tfm-main" tabIndex={-1} className="flex min-h-0 flex-1 flex-col overflow-y-auto outline-none">
          <Suspense fallback={<RouteFallback />}>
            <Outlet />
          </Suspense>
        </main>
        <PlayerSlot />
        <BottomNav />
      </div>

      <MobileDrawer />
      <CommandPaletteHost />
      <ChannelCommands />
    </div>
  );
}
