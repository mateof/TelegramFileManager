import { AuthStep, QrLoginStatus, type AuthStatusDto, type QrLoginDto } from '@tfm/api-client';
import { Button, Field, Input, SegmentedControl, Spinner } from '@tfm/ui';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, KeyRound, MessageSquareCode, Phone, QrCode, RefreshCw, Smartphone } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { AuthShell } from './AuthShell';

type Method = 'phone' | 'qr';

const POLL_MS = 2000;

/** Telegram account login: phone → code → 2FA, or QR (with 2FA). */
export function TelegramLogin({ status }: { status: AuthStatusDto }) {
  const [method, setMethod] = useState<Method>('qr');
  return (
    <AuthShell
      wide
      title={t('Connect your Telegram account')}
      subtitle={t('The server uses your account to read and upload the files of your channels.')}
    >
      <div className="flex flex-col gap-5">
        <SegmentedControl<Method>
          aria-label={t('Login method')}
          fullWidth
          value={method}
          onValueChange={setMethod}
          options={[
            { value: 'qr', label: t('QR code'), icon: <QrCode /> },
            { value: 'phone', label: t('Phone'), icon: <Phone /> }
          ]}
        />
        {method === 'qr' ? <QrLogin /> : <PhoneLogin initialStep={status.step} />}
      </div>
    </AuthShell>
  );
}

function useFinishLogin() {
  const qc = useQueryClient();
  return useCallback(() => {
    void qc.invalidateQueries({ queryKey: queryKeys.telegramStatus });
    void qc.invalidateQueries({ queryKey: queryKeys.channels });
  }, [qc]);
}

// ---- Phone ---------------------------------------------------------------------

function PhoneLogin({ initialStep }: { initialStep: string }) {
  const finish = useFinishLogin();
  const [step, setStep] = useState<string>(
    initialStep === AuthStep.VerificationCode || initialStep === AuthStep.Password ? initialStep : AuthStep.Phone
  );
  const [value, setValue] = useState('');
  const [phone, setPhone] = useState('');

  const send = useMutation({
    mutationFn: () => api.auth.login({ value: value.trim(), isPhone: step === AuthStep.Phone }),
    onSuccess: (res) => {
      if (step === AuthStep.Phone) setPhone(value.trim());
      setValue('');
      if (res.isAuthenticated || res.step === AuthStep.Authenticated) finish();
      else setStep(res.step);
    }
  });

  const meta =
    step === AuthStep.VerificationCode
      ? {
          icon: <MessageSquareCode />,
          label: t('Verification code'),
          hint: t('Telegram sent a code to your other sessions (or by SMS) for {phone}.', { phone: phone || '…' }),
          type: 'text',
          autoComplete: 'one-time-code',
          inputMode: 'numeric' as const,
          placeholder: '12345'
        }
      : step === AuthStep.Password
        ? {
            icon: <KeyRound />,
            label: t('Two-step verification password'),
            hint: t('Your account has a cloud password. Enter it to finish.'),
            type: 'password',
            autoComplete: 'current-password',
            inputMode: undefined,
            placeholder: ''
          }
        : {
            icon: <Phone />,
            label: t('Phone number'),
            hint: t('International format, e.g. +34 600 000 000'),
            type: 'tel',
            autoComplete: 'tel',
            inputMode: 'tel' as const,
            placeholder: '+34 600 000 000'
          };

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        if (value.trim()) send.mutate();
      }}
      className="flex flex-col gap-4"
    >
      <Field label={meta.label} hint={meta.hint} error={send.isError ? errorMessage(send.error) : undefined}>
        {({ id, describedBy, invalid }) => (
          <Input
            key={step}
            id={id}
            type={meta.type}
            inputMode={meta.inputMode}
            autoComplete={meta.autoComplete}
            placeholder={meta.placeholder}
            value={value}
            onChange={(e) => setValue(e.target.value)}
            aria-describedby={describedBy}
            aria-invalid={invalid || undefined}
            leading={meta.icon}
            inputSize="lg"
            autoFocus
          />
        )}
      </Field>
      <div className="flex gap-2">
        {step !== AuthStep.Phone && (
          <Button
            variant="ghost"
            size="lg"
            icon={<ArrowLeft />}
            onClick={() => {
              setStep(AuthStep.Phone);
              setValue('');
              send.reset();
            }}
          >
            {t('Back')}
          </Button>
        )}
        <Button type="submit" size="lg" className="flex-1" loading={send.isPending} disabled={!value.trim()}>
          {step === AuthStep.Phone ? t('Send code') : t('Continue')}
        </Button>
      </div>
    </form>
  );
}

// ---- QR ------------------------------------------------------------------------

function QrLogin() {
  const finish = useFinishLogin();
  const [session, setSession] = useState<QrLoginDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [password, setPassword] = useState('');
  const [attempt, setAttempt] = useState(0);
  const sessionId = useRef<string | null>(null);

  // Start a session (again on retry) and cancel it when leaving the screen
  useEffect(() => {
    const controller = new AbortController();
    api.auth
      .startQr({}, controller.signal)
      .then((s) => {
        sessionId.current = s.sessionId;
        setSession(s);
      })
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(errorMessage(e));
      });
    return () => {
      controller.abort();
      const id = sessionId.current;
      sessionId.current = null;
      if (id) void api.auth.cancelQr(id).catch(() => undefined);
    };
  }, [attempt]);

  // Poll while waiting (Telegram rotates the token ~every 30 s: repaint from the poll)
  const status = session?.status;
  const id = session?.sessionId;
  useEffect(() => {
    if (!id || status !== QrLoginStatus.Waiting) return;
    const controller = new AbortController();
    const timer = setInterval(() => {
      api.auth
        .pollQr(id, controller.signal)
        .then(setSession)
        .catch((e: unknown) => {
          if (!controller.signal.aborted) setError(errorMessage(e));
        });
    }, POLL_MS);
    return () => {
      clearInterval(timer);
      controller.abort();
    };
  }, [id, status]);

  useEffect(() => {
    if (status === QrLoginStatus.Authenticated) {
      sessionId.current = null;
      finish();
    }
  }, [status, finish]);

  const sendPassword = useMutation({
    mutationFn: () => api.auth.qrPassword(id!, { password }),
    onSuccess: (s) => {
      setPassword('');
      setSession(s);
    }
  });

  const retry = () => {
    setError(null);
    setSession(null);
    sendPassword.reset();
    setAttempt((a) => a + 1);
  };

  if (error || status === QrLoginStatus.Error || status === QrLoginStatus.Cancelled) {
    return (
      <div className="flex flex-col items-center gap-4 py-4 text-center">
        <p className="text-sm text-danger">{error ?? session?.error ?? t('The QR session ended.')}</p>
        <Button variant="secondary" icon={<RefreshCw />} onClick={retry}>
          {t('Generate a new code')}
        </Button>
      </div>
    );
  }

  if (status === QrLoginStatus.PasswordRequired) {
    return (
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (password) sendPassword.mutate();
        }}
        className="flex flex-col gap-4"
      >
        <Field
          label={t('Two-step verification password')}
          hint={t('Your account has a cloud password. Enter it to finish.')}
          error={sendPassword.isError ? errorMessage(sendPassword.error) : undefined}
        >
          {({ id: fieldId, describedBy, invalid }) => (
            <Input
              id={fieldId}
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-describedby={describedBy}
              aria-invalid={invalid || undefined}
              leading={<KeyRound />}
              inputSize="lg"
              autoFocus
            />
          )}
        </Field>
        <Button type="submit" size="lg" loading={sendPassword.isPending} disabled={!password}>
          {t('Continue')}
        </Button>
      </form>
    );
  }

  return (
    <div className="flex flex-col items-center gap-5 sm:flex-row sm:items-start">
      <div className="flex size-52 shrink-0 items-center justify-center rounded-2xl bg-white p-3 shadow-elevated">
        {session?.qrImageBase64 ? (
          <img src={`data:image/png;base64,${session.qrImageBase64}`} alt={t('QR code to sign in with Telegram')} className="size-full [image-rendering:pixelated]" />
        ) : (
          <Spinner size="lg" className="text-neutral-900" />
        )}
      </div>
      <ol className="flex flex-col gap-3 text-sm text-fg-muted">
        <li className="flex gap-2.5">
          <Smartphone className="mt-0.5 size-4 shrink-0 text-accent-400" />
          {t('Open Telegram on your phone.')}
        </li>
        <li className="flex gap-2.5">
          <span className="mt-0.5 flex size-4 shrink-0 items-center justify-center rounded-full bg-primary-soft text-[10px] font-bold text-accent-300">2</span>
          {t('Go to Settings → Devices → Link Desktop Device.')}
        </li>
        <li className="flex gap-2.5">
          <span className="mt-0.5 flex size-4 shrink-0 items-center justify-center rounded-full bg-primary-soft text-[10px] font-bold text-accent-300">3</span>
          {t('Point the camera at this code.')}
        </li>
        <li className="mt-1 flex items-center gap-2 text-xs text-fg-subtle">
          <Spinner size="xs" />
          {t('Waiting for confirmation...')}
        </li>
      </ol>
    </div>
  );
}
