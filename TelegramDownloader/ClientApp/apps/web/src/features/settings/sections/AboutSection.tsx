import { Card } from '@tfm/ui';
import { ArrowUpRight, BookOpen, Github, Headphones, MonitorPlay, Smartphone, Tv } from 'lucide-react';
import { SISTER_APPS } from '@/features/apps/links';
import { VersionCard } from '@/features/system/components/VersionCard';
import { t } from '@/i18n';
import { SettingsGroup } from '../components/SettingRow';

const REPO = 'https://github.com/mateof/TelegramFileManager';

export function AboutSection() {
  const links = [
    { href: REPO, label: t('Source code on GitHub'), note: t('Free and open source. Contributions are welcome!'), icon: <Github /> },
    { href: `${REPO}/releases`, label: t('Releases'), note: t('Changelog of every version'), icon: <BookOpen /> },
    { href: '/api-docs', label: t('API documentation'), note: 'Swagger · /api-docs', icon: <BookOpen /> },
    { href: '/', label: t('Open the classic interface'), note: t('The previous Blazor interface keeps working.'), icon: <MonitorPlay />, internal: true },
    { href: SISTER_APPS.player, label: 'TFM Player', note: t('Web · Installable'), icon: <Headphones /> },
    { href: SISTER_APPS.android, label: 'TFM Android', note: 'Android', icon: <Smartphone /> },
    { href: SISTER_APPS.androidTv, label: 'TFM Android TV', note: 'Android TV · Google TV', icon: <Tv /> }
  ];
  return (
    <>
      <div id="setting-version">
        <VersionCard />
      </div>
      <SettingsGroup title={t('Links')}>
        <div id="setting-links" className="grid gap-2 p-3 sm:grid-cols-2">
          {links.map((l) => (
            <Card key={l.href} interactive className="p-0">
              <a href={l.href} {...(l.internal ? {} : { target: '_blank', rel: 'noopener noreferrer' })} className="flex items-center gap-3 p-3">
                <span className="flex size-9 items-center justify-center rounded-xl bg-surface-2 text-accent-300 [&_svg]:size-4.5">{l.icon}</span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-medium text-fg">{l.label}</span>
                  <span className="block truncate text-[11px] text-fg-muted">{l.note}</span>
                </span>
                <ArrowUpRight className="size-4 text-fg-subtle" />
              </a>
            </Card>
          ))}
        </div>
      </SettingsGroup>
    </>
  );
}
