import { Badge, Button, Input } from '@tfm/ui';
import { Eraser, KeyRound, Network, Undo2, User } from 'lucide-react';
import { useId } from 'react';
import { t } from '@/i18n';
import { CopyField } from '../components/Copy';
import { SettingRow, SettingsGroup } from '../components/SettingRow';
import type { FormSectionProps } from './types';

export function WebDavSection({ form, values: v }: FormSectionProps) {
  const userId = useId();
  const passId = useId();
  const passwordSet = form.config.data?.webDavPasswordSet ?? false;
  const base = typeof window !== 'undefined' ? `${window.location.origin}/webdav/` : '/webdav/';
  const pending = v.webDavPassword;

  return (
    <>
      <SettingsGroup title={t('Credentials')} icon={<KeyRound />}>
        <SettingRow id="webDavUser" controlId={userId}>
          <Input id={userId} value={v.webDavUser ?? ''} autoComplete="off" leading={<User />} onChange={(e) => form.setField('webDavUser', e.target.value)} className="w-64" />
        </SettingRow>
        <SettingRow
          id="webDavPassword"
          controlId={passId}
          error={form.errors.webDavPassword}
          badge={
            pending === '' ? (
              <Badge tone="warning">{t('Will be cleared')}</Badge>
            ) : pending ? (
              <Badge tone="info">{t('Will be changed')}</Badge>
            ) : passwordSet ? (
              <Badge tone="success" dot>{t('Set')}</Badge>
            ) : (
              <Badge>{t('Not set')}</Badge>
            )
          }
        >
          <div className="flex items-center gap-1.5">
            <Input
              id={passId}
              type="password"
              autoComplete="new-password"
              placeholder={passwordSet ? '••••••••' : ''}
              value={pending ?? ''}
              onChange={(e) => form.setField('webDavPassword', e.target.value === '' ? null : e.target.value)}
              className="w-56"
            />
            {pending !== null ? (
              <Button variant="ghost" size="sm" icon={<Undo2 />} onClick={() => form.setField('webDavPassword', null)}>
                {t('Keep')}
              </Button>
            ) : (
              passwordSet && (
                <Button variant="ghost" size="sm" icon={<Eraser />} onClick={() => form.setField('webDavPassword', '')}>
                  {t('Clear')}
                </Button>
              )
            )}
          </div>
        </SettingRow>
      </SettingsGroup>
      <SettingsGroup title={t('Address')} icon={<Network />}>
        <SettingRow
          id="webDavUrl"
          stacked
          extra={
            <p className="mt-1 text-xs leading-relaxed text-fg-muted">
              {t('Every channel is a read/write WebDAV share at /webdav/{channelId}/ (Synology Hyper Backup, rclone, davfs2, Windows Explorer…).', { channelId: '{channelId}' })}
            </p>
          }
        >
          <CopyField value={`${base}{channelId}/`} label={t('WebDAV address')} className="max-w-lg" />
        </SettingRow>
      </SettingsGroup>
    </>
  );
}
