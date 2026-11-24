import { ApiErrorCodes, isApiError } from '@tfm/api-client';
import { Badge, Button, confirm, Field, Input, Skeleton, Switch, toast } from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { KeyRound, LogOut, Network, ShieldAlert, ShieldCheck, Trash2 } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { SettingRow, SettingsGroup } from '../components/SettingRow';
import { MIN_WEB_PASSWORD, validateNewPassword } from '../model/configForm';
import { passkeysQueryKey } from '../model/passkeys';
import { PasskeysPanel } from './PasskeysPanel';

function webAuthError(error: unknown): string {
  if (isApiError(error)) {
    if (error.code === ApiErrorCodes.InvalidCredentials) return t('The current password is wrong');
    if (error.code === ApiErrorCodes.TooManyAttempts || error.status === 429) return t('Too many attempts. Wait a moment and try again.');
    if (error.code === 'weak_password') return t('Use at least {count} characters', { count: MIN_WEB_PASSWORD });
  }
  return errorMessage(error);
}

function PasswordForm({ passwordSet, passkeyCount }: { passwordSet: boolean; passkeyCount: number }) {
  const qc = useQueryClient();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [repeat, setRepeat] = useState('');
  const [touched, setTouched] = useState(false);
  const validation = validateNewPassword(next, repeat);

  const reset = () => {
    setCurrent('');
    setNext('');
    setRepeat('');
    setTouched(false);
  };
  const done = (message: string) => {
    reset();
    toast.success(message);
    void qc.invalidateQueries({ queryKey: queryKeys.webAuthStatus });
  };

  const setPassword = useMutation({ mutationFn: () => api.webAuth.setup({ password: next }), onSuccess: () => done(t('Web password set')) });
  const change = useMutation({ mutationFn: () => api.webAuth.changePassword({ currentPassword: current, newPassword: next }), onSuccess: () => done(t('Password changed. Other browsers have been signed out.')) });
  const remove = useMutation({
    mutationFn: () => api.webAuth.removePassword({ currentPassword: current }),
    onSuccess: () => {
      done(t('Web password removed'));
      qc.removeQueries({ queryKey: passkeysQueryKey });
    }
  });
  const busy = setPassword.isPending || change.isPending || remove.isPending;
  const error = setPassword.error ?? change.error ?? remove.error;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (validation || (passwordSet && !current)) return;
    if (passwordSet) change.mutate();
    else setPassword.mutate();
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4 px-5 py-4" noValidate>
      {passwordSet && (
        <Field label={t('Current password')} hint={t('Required to change or remove the password.')} error={touched && !current ? t('Enter the current password') : undefined}>
          {({ id, describedBy, invalid }) => (
            <Input id={id} type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} aria-describedby={describedBy} aria-invalid={invalid || undefined} leading={<KeyRound />} />
          )}
        </Field>
      )}
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={passwordSet ? t('New password') : t('Password')} hint={t('At least {count} characters.', { count: MIN_WEB_PASSWORD })}>
          {({ id, describedBy }) => <Input id={id} type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} aria-describedby={describedBy} />}
        </Field>
        <Field label={t('Repeat the password')} error={touched && validation ? validation : undefined}>
          {({ id, describedBy, invalid }) => (
            <Input id={id} type="password" autoComplete="new-password" value={repeat} onChange={(e) => setRepeat(e.target.value)} aria-describedby={describedBy} aria-invalid={invalid || undefined} />
          )}
        </Field>
      </div>
      {error && (
        <p role="alert" className="text-sm text-danger">
          {webAuthError(error)}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" loading={setPassword.isPending || change.isPending} disabled={busy} icon={<ShieldCheck />}>
          {passwordSet ? t('Change password') : t('Set password')}
        </Button>
        {passwordSet && (
          <Button
            type="button"
            variant="outline"
            icon={<Trash2 />}
            loading={remove.isPending}
            disabled={busy}
            onClick={async () => {
              setTouched(true);
              if (!current) return;
              const ok = await confirm({
                title: t('Remove the web password?'),
                description:
                  passkeyCount > 0
                    ? `${t('Anyone who can reach this server will be able to use the web interface. Every browser is signed out.')} ${t('Your {count} passkeys are removed too.', { count: passkeyCount })}`
                    : t('Anyone who can reach this server will be able to use the web interface. Every browser is signed out.'),
                destructive: true,
                confirmLabel: t('Remove')
              });
              if (ok) remove.mutate();
            }}
          >
            {t('Remove password')}
          </Button>
        )}
      </div>
    </form>
  );
}

export function SecuritySection() {
  const qc = useQueryClient();
  const status = useQuery({ queryKey: queryKeys.webAuthStatus, queryFn: ({ signal }) => api.webAuth.status(signal), staleTime: 30_000 });
  const lan = useMutation({
    mutationFn: (enabled: boolean) => api.webAuth.updateSettings({ lanBypassEnabled: enabled }),
    onMutate: (enabled) => {
      const previous = status.data;
      if (previous) qc.setQueryData(queryKeys.webAuthStatus, { ...previous, lanBypassEnabled: enabled });
      return { previous };
    },
    onError: (error, _v, ctx) => {
      if (ctx?.previous) qc.setQueryData(queryKeys.webAuthStatus, ctx.previous);
      toast.error(t('Could not save the settings'), { description: errorMessage(error) });
    },
    onSuccess: (_d, enabled) => toast.success(enabled ? t('Local network access without password enabled') : t('The password is now required on the local network too')),
    onSettled: () => void qc.invalidateQueries({ queryKey: queryKeys.webAuthStatus })
  });
  const logout = useMutation({
    mutationFn: () => api.webAuth.logout(),
    onSuccess: () => void qc.invalidateQueries({ queryKey: queryKeys.webAuthStatus })
  });
  const lanId = useId();

  if (status.isPending) return <Skeleton className="h-64" />;
  if (status.isError) return <p className="text-sm text-danger">{errorMessage(status.error)}</p>;
  const s = status.data;

  return (
    <>
      <SettingsGroup
        title={t('Web password')}
        icon={s.passwordSet ? <ShieldCheck /> : <ShieldAlert />}
        actions={s.passwordSet ? <Badge tone="success" dot>{t('On')}</Badge> : <Badge tone="warning" dot>{t('Off')}</Badge>}
      >
        <p className="px-5 pt-4 text-xs leading-relaxed text-fg-muted" id="setting-webPassword">
          {s.passwordSet
            ? t('The web interface, the API, the hubs and the media URLs need this password (or the API key, for the apps). Changing or removing it signs out every browser.')
            : t('Without a password anyone who can reach this server can use the web interface. Set one (at least 8 characters) before exposing it outside your network. The apps keep using the API key.')}
        </p>
        <PasswordForm passwordSet={s.passwordSet} passkeyCount={s.passkeyCount ?? 0} />
      </SettingsGroup>

      {s.passwordSet && <PasskeysPanel />}

      <SettingsGroup title={t('Local network')} icon={<Network />}>
        <SettingRow
          id="lanBypass"
          controlId={lanId}
          badge={s.lanBypassActive ? <Badge tone="info">{t('Active on this device')}</Badge> : undefined}
          extra={
            <div className="mt-1 flex flex-col gap-1.5 text-xs leading-relaxed text-fg-muted">
              <p>{t('Devices that connect directly from the local network (private or loopback address, using an IP, localhost or a local name such as nas or *.local) get in without the password.')}</p>
              <p>{t('Requests that come through a reverse proxy always need it: the proxy itself sits on the local network, so otherwise internet traffic would look local too.')}</p>
              {!s.passwordSet && <p className="font-medium text-warning">{t('Only applies once a password is set.')}</p>}
            </div>
          }
        >
          <Switch id={lanId} checked={s.lanBypassEnabled} disabled={lan.isPending} onCheckedChange={(v) => lan.mutate(v)} aria-label={t('No password on the local network')} />
        </SettingRow>
      </SettingsGroup>

      <SettingsGroup title={t('Sessions')} icon={<LogOut />}>
        <SettingRow
          id="signOut"
          label={t('Sign out of this browser')}
          extra={
            <p className="mt-1 text-xs text-fg-muted">
              {s.passwordSet
                ? t('To sign out every other browser at once, change the password: every session is closed.')
                : t('There is no password, so there is no session to close.')}
            </p>
          }
        >
          <Button variant="outline" size="sm" icon={<LogOut />} disabled={!s.passwordSet || s.lanBypassActive} loading={logout.isPending} onClick={() => logout.mutate()}>
            {t('Sign out')}
          </Button>
        </SettingRow>
      </SettingsGroup>
    </>
  );
}
