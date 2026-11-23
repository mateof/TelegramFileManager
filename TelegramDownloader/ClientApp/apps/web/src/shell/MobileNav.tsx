import { cn, CountBadge, Sheet, SheetContent } from '@tfm/ui';
import { Menu } from 'lucide-react';
import { NavLink } from 'react-router';
import { t } from '@/i18n';
import { useRealtime } from '@/lib/realtime';
import { useUi } from '@/stores/ui';
import { mobileNavItems } from './nav';
import { Sidebar } from './Sidebar';

/** Bottom tab bar on phones/tablets (< lg). The last tab opens the drawer. */
export function BottomNav() {
  const setMobileNavOpen = useUi((s) => s.setMobileNavOpen);
  const summary = useRealtime((s) => s.transferSummary);
  const active = summary ? summary.activeDownloads + summary.activeUploads + summary.activeTasks : 0;
  const tab = 'relative flex flex-1 flex-col items-center justify-center gap-0.5 text-[11px] font-medium text-fg-subtle transition-colors';

  return (
    <nav
      aria-label={t('Main navigation')}
      className="glass z-30 flex h-16 shrink-0 items-stretch border-t border-border pb-[env(safe-area-inset-bottom)] lg:hidden"
    >
      {mobileNavItems().map((item) => {
        const Icon = item.icon;
        return (
          <NavLink key={item.id} to={item.to} aria-label={item.label} className={({ isActive }) => cn(tab, isActive && 'text-accent-400')}>
            {({ isActive }) => (
              <>
                <span className={cn('flex h-7 w-12 items-center justify-center rounded-full transition-colors', isActive && 'bg-primary-soft')}>
                  <Icon className="size-5" />
                </span>
                <span className="max-w-full truncate px-0.5">{item.shortLabel ?? item.label}</span>
                {item.id === 'transfers' && <CountBadge count={active} className="absolute right-[calc(50%-1.4rem)] top-1.5" />}
              </>
            )}
          </NavLink>
        );
      })}
      <button type="button" className={tab} onClick={() => setMobileNavOpen(true)}>
        <span className="flex h-7 w-12 items-center justify-center rounded-full">
          <Menu className="size-5" />
        </span>
        {t('More')}
      </button>
    </nav>
  );
}

/** Drawer with the full sidebar (channels + every page) on phones. */
export function MobileDrawer() {
  const open = useUi((s) => s.mobileNavOpen);
  const setOpen = useUi((s) => s.setMobileNavOpen);
  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetContent side="left" title={t('Menu')} hideHeader className="lg:hidden">
        <div className="flex h-full flex-col pt-[env(safe-area-inset-top)]">
          <Sidebar inDrawer onNavigate={() => setOpen(false)} />
        </div>
      </SheetContent>
    </Sheet>
  );
}
