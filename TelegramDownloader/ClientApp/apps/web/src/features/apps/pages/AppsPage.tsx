import { Badge, Button, Card, cn, toast } from '@tfm/ui';
import { useQuery } from '@tanstack/react-query';
import { ArrowUpRight, Check, Copy, Headphones, LayoutGrid, MonitorPlay, Server, Smartphone, Tv } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { t } from '@/i18n';
import { api } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { PageHeader } from '@/shell/PageHeader';
import { SISTER_APPS } from '../links';

interface SisterApp {
  id: string;
  name: string;
  tagline: string;
  description: string;
  url: string;
  cta: string;
  icon: ReactNode;
  platform: string;
  /** Tailwind gradient of the hero tile. */
  gradient: string;
  features: string[];
}


function apps(): SisterApp[] {
  return [
    {
      id: 'player',
      name: 'TFM Player',
      tagline: t('Your music, anywhere'),
      description: t('Installable web app (PWA) to listen to the audio of your channels, with offline downloads, playlists and a lock-screen player.'),
      url: SISTER_APPS.player,
      cta: t('Open TFM Player'),
      icon: <Headphones />,
      platform: t('Web · Installable'),
      gradient: 'from-emerald-400 via-teal-500 to-cyan-600',
      features: [t('Works offline'), t('Gapless playback'), t('Playlists synced with the server')]
    },
    {
      id: 'android',
      name: 'TFM Android',
      tagline: t('The file manager in your pocket'),
      description: t('Native Android app to browse channels, stream media and manage transfers on the go.'),
      url: SISTER_APPS.android,
      cta: t('Download the APK'),
      icon: <Smartphone />,
      platform: 'Android',
      gradient: 'from-lime-400 via-green-500 to-emerald-700',
      features: [t('Browse every channel'), t('Video and audio streaming'), t('Transfer notifications')]
    },
    {
      id: 'tv',
      name: 'TFM Android TV',
      tagline: t('Your library on the big screen'),
      description: t('Lean-back app for Android TV and Google TV to watch the movies and series of your library with the remote.'),
      url: SISTER_APPS.androidTv,
      cta: t('Download for TV'),
      icon: <Tv />,
      platform: 'Android TV · Google TV',
      gradient: 'from-violet-500 via-fuchsia-500 to-rose-500',
      features: [t('Movies and series library'), t('Continue watching'), t('Designed for the remote')]
    }
  ];
}

function CopyButton({ value }: { value: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button
      variant="secondary"
      size="sm"
      icon={copied ? <Check /> : <Copy />}
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(value);
          setCopied(true);
          toast.success(t('Copied to the clipboard'));
          setTimeout(() => setCopied(false), 1600);
        } catch {
          toast.error(t('Could not copy'));
        }
      }}
    >
      {t('Copy')}
    </Button>
  );
}

function AppCard({ app }: { app: SisterApp }) {
  return (
    <Card interactive className="group flex flex-col overflow-hidden">
      <div className={cn('relative flex h-36 items-end overflow-hidden bg-linear-to-br p-5', app.gradient)}>
        <div aria-hidden className="absolute -right-8 -top-10 size-44 rounded-full bg-white/15 blur-2xl transition-transform duration-500 group-hover:scale-125" />
        <div aria-hidden className="absolute -bottom-16 left-10 size-40 rounded-full bg-black/10 blur-2xl" />
        <div className="relative flex size-14 items-center justify-center rounded-2xl bg-white/20 text-white shadow-elevated ring-1 ring-white/30 backdrop-blur-md transition-transform duration-300 group-hover:-translate-y-1 [&_svg]:size-7">
          {app.icon}
        </div>
        <Badge className="absolute right-4 top-4 border-white/25 bg-black/25 text-white backdrop-blur-md">{app.platform}</Badge>
      </div>
      <div className="flex flex-1 flex-col gap-3 p-5">
        <div>
          <h2 className="text-lg font-semibold tracking-tight text-fg">{app.name}</h2>
          <p className="text-sm font-medium text-accent-400">{app.tagline}</p>
        </div>
        <p className="text-sm leading-relaxed text-fg-muted">{app.description}</p>
        <ul className="flex flex-col gap-1.5">
          {app.features.map((f) => (
            <li key={f} className="flex items-center gap-2 text-sm text-fg-muted">
              <span className="flex size-4.5 items-center justify-center rounded-full bg-primary-soft text-accent-300">
                <Check className="size-3" strokeWidth={3} />
              </span>
              {f}
            </li>
          ))}
        </ul>
        <div className="mt-auto flex flex-wrap gap-2 pt-2">
          <Button asChild className="flex-1">
            <a href={app.url} target="_blank" rel="noopener noreferrer">
              {app.cta}
              <ArrowUpRight />
            </a>
          </Button>
          <CopyButton value={app.url} />
        </div>
      </div>
    </Card>
  );
}

export default function AppsPage() {
  const info = useQuery({ queryKey: queryKeys.serverInfo, queryFn: ({ signal }) => api.system.info(signal) });
  const serverUrl = typeof window !== 'undefined' ? window.location.origin : '';

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-8 p-4 sm:p-6 lg:p-8">
      <PageHeader
        title={t('Apps')}
        description={t('Take Telegram File Manager everywhere: the same server, on every screen.')}
        icon={<LayoutGrid />}
      />

      <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
        {apps().map((app) => (
          <AppCard key={app.id} app={app} />
        ))}
      </div>

      <Card glass className="flex flex-col gap-4 p-5 sm:flex-row sm:items-center">
        <div className="flex size-12 shrink-0 items-center justify-center rounded-2xl bg-primary-soft text-accent-300">
          <Server className="size-6" />
        </div>
        <div className="min-w-0 flex-1">
          <h2 className="font-semibold text-fg">{t('Connect an app to this server')}</h2>
          <p className="text-sm text-fg-muted">
            {t('Use this address in the app. If the server has an API key, you will find it in Settings → Apps & API.')}
          </p>
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <code className="rounded-lg border border-border bg-surface-2 px-2.5 py-1 font-mono text-sm text-fg">{serverUrl}</code>
            {info.data?.version && (
              <Badge tone="accent">
                {t('Server')} {info.data.version}
              </Badge>
            )}
          </div>
        </div>
        <CopyButton value={serverUrl} />
      </Card>

      <Card className="flex flex-col items-start gap-3 p-5 sm:flex-row sm:items-center">
        <MonitorPlay className="size-5 shrink-0 text-fg-muted" />
        <p className="flex-1 text-sm text-fg-muted">{t('Prefer the previous interface? The classic UI keeps working.')}</p>
        <Button asChild variant="ghost" size="sm">
          <a href="/">{t('Open the classic interface')}</a>
        </Button>
      </Card>
    </div>
  );
}
