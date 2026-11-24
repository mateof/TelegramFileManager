import { ApiErrorCodes, isApiError } from '@tfm/api-client';
import { Button, Checkbox, Field, Input } from '@tfm/ui';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Eye, EyeOff, FingerprintPattern, KeyRound, LogIn, ShieldCheck } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { AuthShell } from './AuthShell';
import { usePasskeyLogin } from './usePasskeyLogin';

const MIN_PASSWORD_LENGTH = 8;

function PasswordInput(props: { id: string; value: string; onChange: (v: string) => void; autoComplete: string; describedBy?: string; invalid?: boolean; autoFocus?: boolean }) {
  const [visible, setVisible] = useState(false);
  return (
    <Input
      id={props.id}
      type={visible ? 'text' : 'password'}
      value={props.value}
      onChange={(e) => props.onChange(e.target.value)}
      autoComplete={props.autoComplete}
      aria-describedby={props.describedBy}
      aria-invalid={props.invalid || undefined}
      autoFocus={props.autoFocus}
      inputSize="lg"
      leading={<KeyRound />}
      trailing={
        <button
          type="button"
          onClick={() => setVisible((v) => !v)}
          aria-label={visible ? t('Hide password') : t('Show password')}
          className="flex size-7 items-center justify-center rounded-md text-fg-subtle hover:text-fg"
        >
          {visible ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
        </button>
      }
    />
  );
}

function loginError(error: unknown): string {
  if (isApiError(error)) {
    if (error.code === ApiErrorCodes.InvalidCredentials || error.status === 401) return t('Wrong password');
    if (error.code === ApiErrorCodes.TooManyAttempts || error.status === 429) return t('Too many attempts. Wait a moment and try again.');
  }
  return errorMessage(error);
}

/**
 * Web password prompt (cookie session). With `passkeys` (status
 * `passkeysAvailable`) it also offers passkeys: in the password field's
 * autofill (conditional mediation) and with a button.
 */
export function WebLoginScreen({ passkeys = false }: { passkeys?: boolean }) {
  const qc = useQueryClient();
  const [password, setPassword] = useState('');
  const [remember, setRemember] = useState(true);
  const passkey = usePasskeyLogin({ available: passkeys, remember });
  const login = useMutation({
    mutationFn: () => api.webAuth.login({ password, remember }),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.webAuthStatus })
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (password) login.mutate();
  };

  return (
    <AuthShell title={t('Welcome back')} subtitle={t('Enter the password of this server to continue.')}>
      <form onSubmit={submit} className="flex flex-col gap-4">
        {/* Single-user server: a fixed user name helps password managers file the password (and its passkey) */}
        <input type="text" name="username" autoComplete="username" value="TFM" readOnly hidden />
        <Field label={t('Password')} error={login.isError ? loginError(login.error) : undefined}>
          {({ id, describedBy, invalid }) => (
            <PasswordInput
              id={id}
              value={password}
              onChange={setPassword}
              autoComplete={passkey.supported ? 'current-password webauthn' : 'current-password'}
              describedBy={describedBy}
              invalid={invalid}
              autoFocus
            />
          )}
        </Field>
        <Checkbox label={t('Keep me signed in on this device')} checked={remember} onCheckedChange={(v) => setRemember(v === true)} />
        <Button type="submit" size="lg" loading={login.isPending} disabled={!password || passkey.busy} icon={<LogIn />}>
          {t('Sign in')}
        </Button>
        {passkey.supported && (
          <>
            <div className="flex items-center gap-3 text-xs text-fg-subtle" aria-hidden>
              <span className="h-px flex-1 bg-border" />
              {t('or')}
              <span className="h-px flex-1 bg-border" />
            </div>
            <Button type="button" size="lg" variant="secondary" loading={passkey.busy} disabled={login.isPending} icon={<FingerprintPattern />} onClick={() => void passkey.signIn()}>
              {t('Sign in with a passkey')}
            </Button>
            {passkey.error && (
              <p role="alert" className="text-sm text-danger">
                {passkey.error}
              </p>
            )}
          </>
        )}
      </form>
    </AuthShell>
  );
}

/** First visit: define the web password. */
export function WebSetupScreen() {
  const qc = useQueryClient();
  const [password, setPassword] = useState('');
  const [repeat, setRepeat] = useState('');
  const tooShort = password.length > 0 && password.length < MIN_PASSWORD_LENGTH;
  const mismatch = repeat.length > 0 && repeat !== password;
  const valid = password.length >= MIN_PASSWORD_LENGTH && password === repeat;

  const setup = useMutation({
    mutationFn: async () => {
      await api.webAuth.setup({ password });
      // Start the session right away so the user does not type it twice
      await api.webAuth.login({ password, remember: true }).catch(() => undefined);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.webAuthStatus })
  });

  return (
    <AuthShell
      title={t('Protect this server')}
      subtitle={t('Choose a password for the web interface. Apps keep using the API key.')}
    >
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (valid) setup.mutate();
        }}
        className="flex flex-col gap-4"
      >
        <Field label={t('New password')} error={tooShort ? t('Use at least {count} characters', { count: MIN_PASSWORD_LENGTH }) : undefined} hint={t('Use at least {count} characters', { count: MIN_PASSWORD_LENGTH })}>
          {({ id, describedBy, invalid }) => (
            <PasswordInput id={id} value={password} onChange={setPassword} autoComplete="new-password" describedBy={describedBy} invalid={invalid} autoFocus />
          )}
        </Field>
        <Field label={t('Repeat the password')} error={mismatch ? t('The passwords do not match') : undefined}>
          {({ id, describedBy, invalid }) => (
            <PasswordInput id={id} value={repeat} onChange={setRepeat} autoComplete="new-password" describedBy={describedBy} invalid={invalid} />
          )}
        </Field>
        {setup.isError && <p className="text-sm text-danger">{errorMessage(setup.error)}</p>}
        <Button type="submit" size="lg" loading={setup.isPending} disabled={!valid} icon={<ShieldCheck />}>
          {t('Save and continue')}
        </Button>
        <p className="text-xs text-fg-muted">
          {t('You can allow access without password from the local network later, in Settings → Security.')}
        </p>
      </form>
    </AuthShell>
  );
}
