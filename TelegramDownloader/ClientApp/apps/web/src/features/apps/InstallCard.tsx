import { Button, Card } from '@tfm/ui';
import { Download, ShieldAlert, Share } from 'lucide-react';
import { t } from '@/i18n';
import { useInstallMode } from '@/pwa/install';
import { installApp } from '@/pwa/installApp';
import { Logo } from '@/shell/Logo';

/** "Install this web app": hidden once running as the installed app. */
export function InstallCard() {
  const mode = useInstallMode();
  if (mode === 'installed') return null;

  return (
    <Card glass className="flex flex-col gap-4 p-5 sm:flex-row sm:items-center">
      <Logo className="size-12" />
      <div className="min-w-0 flex-1">
        <h2 className="font-semibold text-fg">{t('Install this web app')}</h2>
        <p className="text-sm text-fg-muted">
          {t('Open Telegram File Manager in its own window, from the dock, the start menu or the home screen, like any other app.')}
        </p>
        {mode === 'insecure' && (
          <p className="mt-2 flex items-start gap-2 text-sm text-warning">
            <ShieldAlert className="mt-0.5 size-4 shrink-0" />
            {t('Installing needs HTTPS (or localhost). Open the server through an HTTPS address, for example behind a reverse proxy, to install the app.')}
          </p>
        )}
        {mode === 'ios' && (
          <p className="mt-2 flex items-start gap-2 text-sm text-fg">
            <Share className="mt-0.5 size-4 shrink-0 text-accent-400" />
            {t('On iPhone and iPad: tap Share, then Add to Home Screen.')}
          </p>
        )}
        {mode === 'manual' && (
          <p className="mt-2 text-sm text-fg-subtle">{t('Your browser has not offered the installation. Look for Install app or Add to Home screen in its menu.')}</p>
        )}
      </div>
      {mode === 'prompt' && (
        <Button icon={<Download />} onClick={() => void installApp(mode)}>
          {t('Install')}
        </Button>
      )}
    </Card>
  );
}
