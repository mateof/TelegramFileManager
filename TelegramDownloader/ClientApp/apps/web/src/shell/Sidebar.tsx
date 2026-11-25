import { cn, IconButton, Tooltip } from '@tfm/ui';
import { ChevronDown, PanelLeftClose, PanelLeftOpen } from 'lucide-react';
import { NavLink } from 'react-router';
import { t } from '@/i18n';
import { useUi } from '@/stores/ui';
import { ChannelList } from './ChannelList';
import { Logo } from './Logo';
import { navSections, type NavItem } from './nav';

function NavEntry({ item, collapsed, onNavigate }: { item: NavItem; collapsed: boolean; onNavigate?: () => void }) {
  const Icon = item.icon;
  const link = (
    <NavLink
      to={item.to}
      onClick={onNavigate}
      className={({ isActive }) =>
        cn(
          'relative flex h-9 items-center gap-3 rounded-lg px-2.5 text-sm font-medium text-fg-muted outline-none transition-colors',
          'hover:bg-surface-2 hover:text-fg focus-visible:ring-2 focus-visible:ring-ring',
          isActive && 'bg-primary-soft text-fg [&_svg]:text-accent-400',
          collapsed && 'justify-center px-0'
        )
      }
    >
      <Icon className="size-4.5 shrink-0" />
      {!collapsed && <span className="truncate">{item.label}</span>}
    </NavLink>
  );
  return collapsed ? (
    <Tooltip content={item.label} side="right">
      {link}
    </Tooltip>
  ) : (
    link
  );
}

export interface SidebarProps {
  /** Icon rail (desktop collapsed). */
  collapsed?: boolean;
  /** Rendered inside the mobile drawer. */
  inDrawer?: boolean;
  onNavigate?: () => void;
}

export function Sidebar({ collapsed = false, inDrawer = false, onNavigate }: SidebarProps) {
  const toggleSidebar = useUi((s) => s.toggleSidebar);
  const foldedSections = useUi((s) => s.drawerCollapsedSections);
  const toggleSection = useUi((s) => s.toggleDrawerSection);
  const drawerChannelsExpanded = useUi((s) => s.drawerChannelsExpanded);
  const setDrawerChannelsExpanded = useUi((s) => s.setDrawerChannelsExpanded);
  const sections = navSections();
  // In the drawer the channel list can take the whole height (the pages stay one tap away)
  const channelsOnly = inDrawer && drawerChannelsExpanded;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className={cn('flex h-14 shrink-0 items-center gap-2 px-3', collapsed && 'justify-center px-0')}>
        {!collapsed && (
          <NavLink to="/channels" onClick={onNavigate} className="flex min-w-0 flex-1 items-center gap-2.5 rounded-lg px-1 py-1 outline-none focus-visible:ring-2 focus-visible:ring-ring">
            <Logo className="size-8" />
            <span className="flex min-w-0 flex-col leading-tight">
              <span className="truncate text-sm font-semibold tracking-tight text-fg">Telegram File Manager</span>
              <span className="text-[11px] text-fg-subtle">v{import.meta.env.APP_VERSION}</span>
            </span>
          </NavLink>
        )}
        {!inDrawer && (
          <IconButton
            label={collapsed ? t('Expand sidebar') : t('Collapse sidebar')}
            size="sm"
            onClick={toggleSidebar}
          >
            {collapsed ? <PanelLeftOpen /> : <PanelLeftClose />}
          </IconButton>
        )}
      </div>

      {!channelsOnly && (
        <>
          <nav aria-label={t('Main navigation')} className={cn('flex min-h-0 shrink flex-col overflow-y-auto px-2 pb-2', inDrawer ? 'gap-1' : 'gap-3')}>
            {sections.map((section) => {
              // Sections fold in the drawer only (remembered), so the channels come up on a phone
              const folded = inDrawer && foldedSections.includes(section.id);
              return (
                <div key={section.id} className="flex flex-col gap-0.5">
                  {inDrawer ? (
                    <button
                      type="button"
                      aria-expanded={!folded}
                      onClick={() => toggleSection(section.id)}
                      className="flex h-8 shrink-0 items-center gap-1.5 rounded-lg px-2.5 text-[11px] font-semibold uppercase tracking-wider text-fg-subtle outline-none transition-colors hover:bg-surface-2 hover:text-fg focus-visible:ring-2 focus-visible:ring-ring"
                    >
                      <span className="min-w-0 flex-1 truncate text-left">{section.label}</span>
                      {folded && <span className="font-normal normal-case tracking-normal">{section.items.length}</span>}
                      <ChevronDown aria-hidden className={cn('size-3.5 shrink-0 transition-transform', folded && '-rotate-90')} />
                    </button>
                  ) : (
                    !collapsed && (
                      <span className="px-2.5 pb-1 pt-1.5 text-[11px] font-semibold uppercase tracking-wider text-fg-subtle">{section.label}</span>
                    )
                  )}
                  {!folded && section.items.map((item) => <NavEntry key={item.id} item={item} collapsed={collapsed} onNavigate={onNavigate} />)}
                </div>
              );
            })}
          </nav>

          <div className="mx-3 h-px shrink-0 bg-border" />
        </>
      )}
      <ChannelList
        collapsed={collapsed}
        onNavigate={onNavigate}
        expanded={channelsOnly}
        onExpandedChange={inDrawer ? setDrawerChannelsExpanded : undefined}
      />
    </div>
  );
}
