import { Badge, Button, Card, confirm, Field, Input } from '@tfm/ui';
import { useQuery } from '@tanstack/react-query';
import { ArrowUpRight, Headphones, KeyRound, RefreshCw, Smartphone, Trash2, Tv } from 'lucide-react';
import { useState } from 'react';
import { SISTER_APPS } from '@/features/apps/links';
import { t } from '@/i18n';
import { api } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { CopyField, QrCode } from '../components/Copy';
import { SettingRow, SettingsGroup } from '../components/SettingRow';
import { generateApiKey, validateApiKey } from '../model/configForm';
import { useImmediateConfig } from '../model/useSettingsForm';
import type { FormSectionProps } from './types';

/** Text encoded in the connection QR: the server address and, when known, the key. */
function connectionPayload(server: string, apiKey: string | null): string {
  return JSON.stringify(apiKey ? { server, apiKey } : { server });
}

export function AppsSection({ form }: FormSectionProps) {
  const config = form.config.data;
  const save = useImmediateConfig();
  const info = useQuery({ queryKey: queryKeys.serverInfo, queryFn: ({ signal }) => api.system.info(signal) });
  const [revealed, setRevealed] = useState<string | null>(null);
  const [custom, setCustom] = useState('');
  const [customError, setCustomError] = useState<string | null>(null);
  const server = typeof window !== 'undefined' ? window.location.origin : '';

  const apply = (key: string) =>
    save.mutate({ mobileApiKey: key }, { onSuccess: () => setRevealed(key || null) });

  const regenerate = async () => {
    if (config?.mobileApiKeySet) {
      const ok = await confirm({
        title: t('Generate a new API key?'),
        description: t('Apps using the current key stop connecting until you enter the new one.'),
        confirmLabel: t('Generate'),
        destructive: true
      });
      if (!ok) return;
    }
    apply(generateApiKey());
  };

  const setCustomKey = () => {
    const error = validateApiKey(custom);
    setCustomError(error);
    if (error) return;
    apply(custom.trim());
    setCustom('');
  };

  const removeKey = async () => {
    const ok = await confirm({
      title: t('Remove the API key?'),
      description: t('The key of config.json applies again, if there is one. Without any key the API is open (unless a web password is set, then the apps cannot connect).'),
      confirmLabel: t('Remove'),
      destructive: true
    });
    if (ok) apply('');
  };

  return (
    <>
      <SettingsGroup title={t('API key for apps')} icon={<KeyRound />}>
        <SettingRow
          id="apiKey"
          stacked
          badge={config?.mobileApiKeySet ? <Badge tone="success" dot>{t('Configured')}</Badge> : <Badge tone="warning" dot>{t('Not set')}</Badge>}
          extra={
            <p className="mt-1 text-xs leading-relaxed text-fg-muted">
              {t('Sent in the X-Api-Key header to /api/v1, /api/mobile and /hubs. While a web password is set, the apps (Android, TV, TFM Player) can only connect with this key. It overrides mobile_api_key of config.json.')}
            </p>
          }
        >
          <div className="flex flex-col gap-4">
            <div className="flex flex-wrap items-center gap-2">
              <code className="rounded-lg border border-border bg-surface-2 px-3 py-1.5 font-mono text-sm text-fg">
                {revealed ?? config?.mobileApiKey ?? (config?.mobileApiKeySet ? t('Set in config.json') : t('None'))}
              </code>
              <Button size="sm" icon={<RefreshCw />} loading={save.isPending} onClick={() => void regenerate()}>
                {config?.mobileApiKeySet ? t('Regenerate') : t('Generate key')}
              </Button>
              {config?.mobileApiKey && (
                <Button size="sm" variant="ghost" icon={<Trash2 />} disabled={save.isPending} onClick={() => void removeKey()}>
                  {t('Remove')}
                </Button>
              )}
            </div>
            {!revealed && config?.mobileApiKeySet && (
              <p className="text-xs text-fg-subtle">{t('For security the saved key is never shown again. Generate a new one to see it in full.')}</p>
            )}
            <form
              className="flex flex-wrap items-end gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                setCustomKey();
              }}
            >
              <Field label={t('Or set your own key')} error={customError ?? undefined} className="min-w-56 flex-1">
                {({ id, describedBy, invalid }) => (
                  <Input id={id} value={custom} autoComplete="off" spellCheck={false} onChange={(e) => setCustom(e.target.value)} aria-describedby={describedBy} aria-invalid={invalid || undefined} />
                )}
              </Field>
              <Button type="submit" variant="secondary" disabled={!custom.trim() || save.isPending}>
                {t('Save key')}
              </Button>
            </form>
          </div>
        </SettingRow>
      </SettingsGroup>

      <SettingsGroup title={t('Connect an app')} icon={<Smartphone />} description={t('Scan or copy these values into the app.')}>
        <div className="flex flex-col gap-5 px-5 py-4 sm:flex-row sm:items-start">
          <QrCode value={connectionPayload(server, revealed)} label={t('QR code with the server address')} />
          <div className="flex min-w-0 flex-1 flex-col gap-3">
            <div>
              <p className="mb-1 text-xs font-medium text-fg-muted">{t('Server address')}</p>
              <CopyField value={server} label={t('Server address')} />
            </div>
            {revealed && (
              <div>
                <p className="mb-1 text-xs font-medium text-fg-muted">{t('API key')}</p>
                <CopyField value={revealed} label={t('API key')} />
                <p className="mt-1.5 text-xs text-warning">{t('Copy it now: it will not be shown again after you leave this page.')}</p>
              </div>
            )}
            <p className="text-xs text-fg-subtle">
              {revealed ? t('The QR code contains the address and the key as JSON.') : t('The QR code contains the server address. Generate a key to include it.')}
              {info.data?.requiresApiKey === false && ` ${t('This server does not require an API key right now.')}`}
            </p>
          </div>
        </div>
      </SettingsGroup>

      <SettingsGroup title={t('TFM apps')}>
        <div id="setting-sisterApps" className="grid gap-3 p-4 sm:grid-cols-3">
          {[
            { name: 'TFM Player', url: SISTER_APPS.player, icon: <Headphones />, note: t('Web · Installable') },
            { name: 'TFM Android', url: SISTER_APPS.android, icon: <Smartphone />, note: 'Android' },
            { name: 'TFM Android TV', url: SISTER_APPS.androidTv, icon: <Tv />, note: 'Android TV · Google TV' }
          ].map((app) => (
            <Card key={app.name} interactive className="p-0">
              <a href={app.url} target="_blank" rel="noopener noreferrer" className="flex items-center gap-3 p-3">
                <span className="flex size-10 items-center justify-center rounded-xl bg-primary-soft text-accent-300 [&_svg]:size-5">{app.icon}</span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-medium text-fg">{app.name}</span>
                  <span className="block truncate text-[11px] text-fg-muted">{app.note}</span>
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
