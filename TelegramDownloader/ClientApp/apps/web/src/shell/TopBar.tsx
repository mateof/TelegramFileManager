import {
  Avatar,
  Badge,
  Button,
  cn,
  CountBadge,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
  IconButton,
  Kbd,
  Popover,
  PopoverContent,
  PopoverTrigger,
  Progress,
  useAppearance,
  useResolvedTheme
} from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  ArrowDown,
  ArrowDownUp,
  ArrowUp,
  Bell,
  BellOff,
  Languages,
  LayoutGrid,
  LogOut,
  Menu,
  Monitor,
  Moon,
  Palette,
  Search,
  Settings,
  Sun,
  Undo2
} from 'lucide-react';
import { Link, useNavigate } from 'react-router';
import { useTelegramStatus } from '@/auth/useTelegramStatus';
import { getLanguage, LANGUAGES, setLanguage, t, type Language } from '@/i18n';
import { api } from '@/lib/api';
import { formatRelative } from '@/lib/format';
import { queryKeys } from '@/lib/queryKeys';
import { stopRealtime, useRealtime } from '@/lib/realtime';
import { useUi } from '@/stores/ui';

const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform);

function SearchTrigger() {
  const setCommandOpen = useUi((s) => s.setCommandOpen);
  return (
    <button
      type="button"
      onClick={() => setCommandOpen(true)}
      className="group flex h-9 w-full max-w-md items-center gap-2.5 rounded-xl border border-border bg-surface-2/50 px-3 text-sm text-fg-subtle transition-colors hover:border-border-strong/60 hover:bg-surface-2 hover:text-fg-muted focus-visible:outline-2 focus-visible:outline-ring"
    >
      <Search className="size-4" />
      <span className="flex-1 truncate text-left">{t('Search channels, pages and actions...')}</span>
      <span className="hidden items-center gap-1 sm:flex">
        <Kbd>{isMac ? '⌘' : 'Ctrl'}</Kbd>
        <Kbd>K</Kbd>
      </span>
    </button>
  );
}

function TransfersIndicator() {
  const summary = useRealtime((s) => s.transferSummary);
  const status = useRealtime((s) => s.transfersStatus);
  const active = summary ? summary.activeDownloads + summary.activeUploads + summary.activeTasks : 0;
  const queued = summary ? summary.queuedDownloads + summary.queuedUploads : 0;
  const working = active > 0;

  return (
    <Popover>
      <PopoverTrigger asChild>
        <button
          type="button"
          aria-label={t('Transfers')}
          className={cn(
            'relative flex h-9 items-center gap-2 rounded-xl px-2.5 text-sm text-fg-muted transition-colors hover:bg-surface-2 hover:text-fg focus-visible:outline-2 focus-visible:outline-ring',
            working && 'bg-primary-soft text-fg'
          )}
        >
          <ArrowDownUp className={cn('size-4.5', working && 'text-accent-400')} />
          {working && summary && (
            <span className="hidden items-center gap-2 text-xs font-medium tabular-nums md:flex">
              <span className="flex items-center gap-0.5">
                <ArrowDown className="size-3" />
                {summary.downloadSpeed}
              </span>
              <span className="flex items-center gap-0.5">
                <ArrowUp className="size-3" />
                {summary.uploadSpeed}
              </span>
            </span>
          )}
          <CountBadge count={active} className="absolute -right-0.5 -top-0.5 md:hidden" />
          {status !== 'connected' && status !== 'idle' && (
            <span aria-hidden className="absolute bottom-1.5 right-1.5 size-1.5 rounded-full bg-warning" />
          )}
        </button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-semibold">{t('Transfers')}</h3>
          <Badge tone={status === 'connected' ? 'success' : 'warning'} dot>
            {status === 'connected' ? t('Live') : t('Reconnecting...')}
          </Badge>
        </div>
        {summary ? (
          <div className="mt-3 flex flex-col gap-3 text-sm">
            <div className="grid grid-cols-2 gap-2">
              <Stat label={t('Downloads')} value={summary.activeDownloads} extra={summary.queuedDownloads ? t('{count} queued', { count: summary.queuedDownloads }) : undefined} speed={summary.downloadSpeed} />
              <Stat label={t('Uploads')} value={summary.activeUploads} extra={summary.queuedUploads ? t('{count} queued', { count: summary.queuedUploads }) : undefined} speed={summary.uploadSpeed} />
            </div>
            {summary.totalTasks > 0 && (
              <div className="flex flex-col gap-1.5">
                <span className="text-xs text-fg-muted">{t('{active} of {total} tasks running', { active: summary.activeTasks, total: summary.totalTasks })}</span>
                <Progress value={(summary.activeTasks / summary.totalTasks) * 100} size="xs" aria-label={t('Tasks')} />
              </div>
            )}
            {summary.downloadsPaused && <Badge tone="warning">{t('Downloads paused')}</Badge>}
            {!working && queued === 0 && <p className="text-xs text-fg-muted">{t('Nothing is transferring right now.')}</p>}
          </div>
        ) : (
          <p className="mt-3 text-xs text-fg-muted">{t('Connecting to the server...')}</p>
        )}
        <Button asChild variant="secondary" size="sm" className="mt-3 w-full">
          <Link to="/transfers">{t('Open transfers')}</Link>
        </Button>
      </PopoverContent>
    </Popover>
  );
}

function Stat({ label, value, extra, speed }: { label: string; value: number; extra?: string; speed: string }) {
  return (
    <div className="rounded-xl border border-border bg-surface-2/50 p-2.5">
      <div className="text-xs text-fg-muted">{label}</div>
      <div className="text-lg font-semibold tabular-nums">{value}</div>
      <div className="text-[11px] text-fg-subtle">{extra ?? speed}</div>
    </div>
  );
}

function NotificationsBell() {
  const notifications = useRealtime((s) => s.notifications);
  const markAllRead = useRealtime((s) => s.markAllRead);
  const clear = useRealtime((s) => s.clearNotifications);
  const unread = notifications.filter((n) => !n.read).length;
  const lang = getLanguage();
  const toneOf = (level: string) =>
    level === 'error' ? 'bg-danger' : level === 'warning' ? 'bg-warning' : level === 'success' ? 'bg-success' : 'bg-info';

  return (
    <Popover onOpenChange={(open) => !open && markAllRead()}>
      <PopoverTrigger asChild>
        <IconButton label={unread ? t('{count} unread notifications', { count: unread }) : t('Notifications')} className="relative">
          <Bell />
          <CountBadge count={unread} className="absolute right-0.5 top-0.5" />
        </IconButton>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-[min(22rem,92vw)] p-0">
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h3 className="text-sm font-semibold">{t('Notifications')}</h3>
          {notifications.length > 0 && (
            <Button variant="ghost" size="xs" onClick={clear}>
              {t('Clear')}
            </Button>
          )}
        </div>
        {notifications.length === 0 ? (
          <div className="flex flex-col items-center gap-2 px-4 py-10 text-center text-sm text-fg-muted">
            <BellOff className="size-5 text-fg-subtle" />
            {t('No notifications')}
          </div>
        ) : (
          <ul className="max-h-96 overflow-y-auto py-1">
            {notifications.map((n) => (
              <li key={n.id} className="flex gap-3 px-4 py-2.5 hover:bg-surface-2/60">
                <span aria-hidden className={cn('mt-1.5 size-2 shrink-0 rounded-full', toneOf(n.level), n.read && 'opacity-40')} />
                <div className="min-w-0 flex-1">
                  {n.title && <p className="truncate text-sm font-medium">{n.title}</p>}
                  <p className="text-sm text-fg-muted">{n.message}</p>
                  <p className="mt-0.5 text-[11px] text-fg-subtle">{formatRelative(n.at, lang)}</p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </PopoverContent>
    </Popover>
  );
}

function ThemeToggle() {
  const toggleLight = useAppearance((s) => s.toggleLight);
  const { isLight } = useResolvedTheme();
  return (
    <IconButton label={isLight ? t('Dark theme') : t('Light theme')} onClick={toggleLight}>
      {isLight ? <Moon /> : <Sun />}
    </IconButton>
  );
}

function UserMenu() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const telegram = useTelegramStatus();
  const webAuth = useQuery({ queryKey: queryKeys.webAuthStatus, queryFn: ({ signal }) => api.webAuth.status(signal), enabled: false });
  const background = useAppearance((s) => s.background);
  const setBackground = useAppearance((s) => s.setBackground);
  const user = telegram.data?.user;
  const name = user ? [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username || '' : '';

  const logout = useMutation({
    mutationFn: () => api.webAuth.logout(),
    onSettled: async () => {
      await stopRealtime();
      qc.clear();
      await qc.invalidateQueries({ queryKey: queryKeys.webAuthStatus });
    }
  });
  const canSignOut = webAuth.data ? webAuth.data.passwordSet && !webAuth.data.lanBypassActive : true;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          aria-label={t('Account menu')}
          className="flex items-center gap-2 rounded-full p-0.5 outline-none transition-shadow hover:ring-2 hover:ring-border focus-visible:ring-2 focus-visible:ring-ring"
        >
          <Avatar name={name || 'TFM'} size="sm" />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-64">
        <DropdownMenuLabel className="normal-case tracking-normal">
          <span className="block truncate text-sm font-semibold text-fg">{name || t('Telegram account')}</span>
          {user?.username && <span className="block truncate text-xs font-normal text-fg-muted">@{user.username}</span>}
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem icon={<Settings />} onSelect={() => navigate('/settings')}>
          {t('Settings')}
        </DropdownMenuItem>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger icon={<Palette />}>{t('Theme')}</DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={background} onValueChange={(v) => setBackground(v as typeof background)}>
              <DropdownMenuRadioItem value="system">
                <Monitor className="size-4" /> {t('System')}
              </DropdownMenuRadioItem>
              <DropdownMenuRadioItem value="light">{t('Light')}</DropdownMenuRadioItem>
              <DropdownMenuRadioItem value="slate">{t('Dark')}</DropdownMenuRadioItem>
              <DropdownMenuRadioItem value="midnight">{t('Midnight')}</DropdownMenuRadioItem>
              <DropdownMenuRadioItem value="amoled">{t('AMOLED black')}</DropdownMenuRadioItem>
            </DropdownMenuRadioGroup>
            <DropdownMenuSeparator />
            <DropdownMenuItem onSelect={() => navigate('/settings/appearance')}>{t('More appearance options...')}</DropdownMenuItem>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger icon={<Languages />}>{t('Language')}</DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={getLanguage()} onValueChange={(v) => setLanguage(v as Language)}>
              {LANGUAGES.map((l) => (
                <DropdownMenuRadioItem key={l.code} value={l.code}>
                  {l.label}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuItem icon={<LayoutGrid />} onSelect={() => navigate('/apps')}>
          {t('Apps')}
        </DropdownMenuItem>
        <DropdownMenuItem icon={<Undo2 />} onSelect={() => window.location.assign('/')}>
          {t('Classic UI')}
        </DropdownMenuItem>
        {canSignOut && (
          <>
            <DropdownMenuSeparator />
            <DropdownMenuItem icon={<LogOut />} destructive onSelect={() => logout.mutate()}>
              {t('Sign out')}
            </DropdownMenuItem>
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export function TopBar() {
  const setMobileNavOpen = useUi((s) => s.setMobileNavOpen);
  return (
    <header className="glass sticky top-0 z-30 flex h-14 shrink-0 items-center gap-2 border-b border-border px-3 pt-[env(safe-area-inset-top)] sm:px-4">
      <IconButton label={t('Open menu')} className="lg:hidden" onClick={() => setMobileNavOpen(true)}>
        <Menu />
      </IconButton>
      <div className="flex min-w-0 flex-1 justify-center lg:justify-start">
        <SearchTrigger />
      </div>
      <div className="flex items-center gap-1">
        <TransfersIndicator />
        <NotificationsBell />
        <ThemeToggle />
        <a
          href="/"
          className="hidden h-8 items-center rounded-lg px-2.5 text-xs font-medium text-fg-muted transition-colors hover:bg-surface-2 hover:text-fg xl:flex"
        >
          {t('Classic UI')}
        </a>
        <UserMenu />
      </div>
    </header>
  );
}
