import { isApiError } from '@tfm/api-client';
import { Button, cn, Field, Input, Spinner } from '@tfm/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, ArrowRight, Check, Database, ExternalLink, KeyRound, Rocket, Send, ShieldCheck } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import { useNavigate } from 'react-router';
import { AuthShell } from '@/auth/AuthShell';
import { validateNewPassword } from '@/features/settings/model/configForm';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { initialStep, setupError, validateApiHash, validateApiId, validateMongo, type WizardStep } from '../validation';

const STEPS: { id: WizardStep; label: () => string; icon: ReactNode }[] = [
  { id: 'mongo', label: () => t('Database'), icon: <Database /> },
  { id: 'telegram', label: () => t('Telegram'), icon: <Send /> },
  { id: 'password', label: () => t('Security'), icon: <ShieldCheck /> },
  { id: 'done', label: () => t('Complete'), icon: <Check /> }
];

function Stepper({ step }: { step: WizardStep }) {
  const index = STEPS.findIndex((s) => s.id === step);
  return (
    <ol className="mb-6 flex items-center justify-between gap-1" aria-label={t('Setup progress')}>
      {STEPS.map((s, i) => (
        <li key={s.id} className="flex flex-1 items-center gap-1 last:flex-none" aria-current={i === index ? 'step' : undefined}>
          <span className="flex flex-col items-center gap-1">
            <span
              className={cn(
                'flex size-8 items-center justify-center rounded-full border text-xs transition-colors duration-300 [&_svg]:size-4',
                i < index && 'border-success bg-success text-white',
                i === index && 'border-accent-500 bg-primary text-on-accent shadow-glow',
                i > index && 'border-border text-fg-subtle'
              )}
            >
              {i < index ? <Check /> : s.icon}
            </span>
            <span className={cn('text-[10px] font-medium', i === index ? 'text-fg' : 'text-fg-subtle')}>{s.label()}</span>
          </span>
          {i < STEPS.length - 1 && <span className={cn('mb-4 h-px flex-1 transition-colors', i < index ? 'bg-success' : 'bg-border')} />}
        </li>
      ))}
    </ol>
  );
}

function ErrorBox({ message }: { message: string | null }) {
  if (!message) return null;
  return (
    <p role="alert" className="rounded-lg border border-danger/30 bg-danger-soft px-3 py-2 text-sm text-danger">
      {message}
    </p>
  );
}

function MongoStep({ onDone }: { onDone: () => void }) {
  const [value, setValue] = useState('mongodb://');
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const invalid = validateMongo(value);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (invalid) return;
    setBusy(true);
    setError(null);
    try {
      const test = await api.system.testMongo({ connectionString: value.trim() });
      if (!test.success) {
        setError(test.error ?? t('Could not connect to MongoDB'));
        return;
      }
      await api.system.saveSetup({ mongoConnectionString: value.trim() });
      onDone();
    } catch (err) {
      setError(setupError(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="flex flex-col gap-4" noValidate>
      <div>
        <h2 className="text-base font-semibold text-fg">{t('MongoDB connection')}</h2>
        <p className="mt-1 text-sm text-fg-muted">{t('Telegram File Manager stores the index of your files and its settings in MongoDB. Enter the connection string of your instance.')}</p>
      </div>
      <Field label={t('Connection string')} hint={t('Example: mongodb://localhost:27017 or mongodb://user:password@host:27017')} error={touched ? (invalid ?? undefined) : undefined}>
        {({ id, describedBy, invalid: inv }) => (
          <Input id={id} value={value} onChange={(e) => setValue(e.target.value)} autoFocus spellCheck={false} autoComplete="off" leading={<Database />} placeholder="mongodb://localhost:27017" aria-describedby={describedBy} aria-invalid={inv || undefined} />
        )}
      </Field>
      <ErrorBox message={error} />
      <Button type="submit" size="lg" loading={busy} iconRight={<ArrowRight />}>
        {busy ? t('Testing connection...') : t('Test & continue')}
      </Button>
    </form>
  );
}

function TelegramStep({ onDone, onBack }: { onDone: () => void; onBack?: () => void }) {
  const [apiId, setApiId] = useState('');
  const [apiHash, setApiHash] = useState('');
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const idError = validateApiId(apiId);
  const hashError = validateApiHash(apiHash);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (idError || hashError) return;
    setBusy(true);
    setError(null);
    try {
      const body = { apiId: apiId.trim(), apiHash: apiHash.trim() };
      const test = await api.system.testTelegram(body);
      if (!test.success) {
        setError(test.error ?? t('Invalid Telegram credentials'));
        return;
      }
      await api.system.saveSetup(body);
      try {
        await api.system.completeSetup();
      } catch (err) {
        // 409: the server already marked it complete by itself
        if (!(isApiError(err) && err.status === 409)) throw err;
      }
      onDone();
    } catch (err) {
      setError(setupError(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="flex flex-col gap-4" noValidate>
      <div>
        <h2 className="text-base font-semibold text-fg">{t('Telegram API credentials')}</h2>
        <p className="mt-1 text-sm text-fg-muted">{t('To connect to Telegram the server needs your own API credentials.')}</p>
      </div>
      <ol className="list-decimal space-y-1 rounded-xl border border-border bg-surface-2/60 py-3 pl-8 pr-3 text-xs text-fg-muted">
        <li>
          {t('Go to')}{' '}
          <a href="https://my.telegram.org/apps" target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-0.5 text-accent-400 hover:text-accent-300">
            my.telegram.org/apps <ExternalLink className="size-3" />
          </a>
        </li>
        <li>{t('Log in with your phone number')}</li>
        <li>{t('Create a new application if you do not have one')}</li>
        <li>{t('Copy the api_id and the api_hash')}</li>
      </ol>
      <Field label="API ID" error={touched ? (idError ?? undefined) : undefined}>
        {({ id, describedBy, invalid }) => (
          <Input id={id} value={apiId} onChange={(e) => setApiId(e.target.value)} inputMode="numeric" autoFocus autoComplete="off" placeholder="12345678" aria-describedby={describedBy} aria-invalid={invalid || undefined} />
        )}
      </Field>
      <Field label="API Hash" error={touched ? (hashError ?? undefined) : undefined}>
        {({ id, describedBy, invalid }) => (
          <Input id={id} value={apiHash} onChange={(e) => setApiHash(e.target.value)} spellCheck={false} autoComplete="off" leading={<KeyRound />} placeholder="0123456789abcdef0123456789abcdef" aria-describedby={describedBy} aria-invalid={invalid || undefined} />
        )}
      </Field>
      <ErrorBox message={error} />
      <div className="flex gap-2">
        {onBack && (
          <Button type="button" variant="secondary" size="lg" icon={<ArrowLeft />} onClick={onBack} disabled={busy}>
            {t('Back')}
          </Button>
        )}
        <Button type="submit" size="lg" loading={busy} iconRight={<ArrowRight />} className="flex-1">
          {busy ? t('Validating...') : t('Save & continue')}
        </Button>
      </div>
    </form>
  );
}

function PasswordStep({ onDone }: { onDone: () => void }) {
  const qc = useQueryClient();
  const [password, setPassword] = useState('');
  const [repeat, setRepeat] = useState('');
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const invalid = validateNewPassword(password, repeat);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (invalid) return;
    setBusy(true);
    setError(null);
    try {
      await api.webAuth.setup({ password });
      await qc.invalidateQueries({ queryKey: queryKeys.webAuthStatus });
      onDone();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="flex flex-col gap-4" noValidate>
      <div>
        <h2 className="text-base font-semibold text-fg">{t('Protect the web interface')}</h2>
        <p className="mt-1 text-sm text-fg-muted">{t('Without a password anyone who can reach this server can use it. You can also set it later in Settings → Web security.')}</p>
      </div>
      <Field label={t('Password')} hint={t('At least {count} characters.', { count: 8 })}>
        {({ id, describedBy }) => <Input id={id} type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} autoFocus aria-describedby={describedBy} />}
      </Field>
      <Field label={t('Repeat the password')} error={touched ? (invalid ?? undefined) : undefined}>
        {({ id, describedBy, invalid: inv }) => (
          <Input id={id} type="password" autoComplete="new-password" value={repeat} onChange={(e) => setRepeat(e.target.value)} aria-describedby={describedBy} aria-invalid={inv || undefined} />
        )}
      </Field>
      <ErrorBox message={error} />
      <div className="flex gap-2">
        <Button type="button" variant="ghost" size="lg" onClick={onDone} disabled={busy}>
          {t('Skip')}
        </Button>
        <Button type="submit" size="lg" loading={busy} icon={<ShieldCheck />} className="flex-1">
          {t('Set password')}
        </Button>
      </div>
    </form>
  );
}

function DoneStep({ alreadyConfigured }: { alreadyConfigured: boolean }) {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const go = async () => {
    await Promise.all([
      qc.invalidateQueries({ queryKey: queryKeys.setupStatus }),
      qc.invalidateQueries({ queryKey: queryKeys.telegramStatus }),
      qc.invalidateQueries({ queryKey: queryKeys.serverInfo })
    ]);
    navigate('/', { replace: true });
  };
  return (
    <div className="flex flex-col items-center gap-4 text-center">
      <span className="flex size-14 animate-pop-in items-center justify-center rounded-full bg-success-soft text-success">
        <Rocket className="size-7" />
      </span>
      <div>
        <h2 className="text-lg font-semibold text-fg">{alreadyConfigured ? t('This server is already configured') : t('Setup complete!')}</h2>
        <p className="mt-1 text-sm text-fg-muted">{t('Next, sign in to your Telegram account.')}</p>
      </div>
      <ul className="w-full space-y-1.5 rounded-xl border border-border bg-surface-2/60 p-3 text-left text-sm">
        <li className="flex items-center gap-2 text-fg">
          <Check className="size-4 text-success" /> {t('MongoDB: connected')}
        </li>
        <li className="flex items-center gap-2 text-fg">
          <Check className="size-4 text-success" /> {t('Telegram API: configured')}
        </li>
      </ul>
      <Button size="lg" className="w-full" iconRight={<ArrowRight />} onClick={() => void go()}>
        {t('Continue to the Telegram login')}
      </Button>
    </div>
  );
}

/** First-run wizard: MongoDB → Telegram API → optional web password → Telegram login. */
export default function SetupPage() {
  const qc = useQueryClient();
  const status = useQuery({ queryKey: queryKeys.setupStatus, queryFn: ({ signal }) => api.system.setup(signal), staleTime: Infinity });
  const webAuth = useQuery({ queryKey: queryKeys.webAuthStatus, queryFn: ({ signal }) => api.webAuth.status(signal), staleTime: Infinity, retry: false });
  const [step, setStep] = useState<WizardStep | null>(null);

  if (status.isPending) {
    return (
      <AuthShell title={t('First-run setup')}>
        <div className="flex justify-center py-8">
          <Spinner size="lg" />
        </div>
      </AuthShell>
    );
  }
  if (status.isError) {
    return (
      <AuthShell title={t('First-run setup')} subtitle={errorMessage(status.error)}>
        <Button className="w-full" onClick={() => void status.refetch()}>
          {t('Retry')}
        </Button>
      </AuthShell>
    );
  }

  const start = initialStep(status.data);
  const current = step ?? start;
  const needsPassword = webAuth.data ? !webAuth.data.passwordSet : false;
  const afterTelegram = () => {
    void qc.invalidateQueries({ queryKey: queryKeys.setupStatus });
    setStep(needsPassword ? 'password' : 'done');
  };

  return (
    <AuthShell title={t('First-run setup')} subtitle={t('Configure your server to get started.')} wide>
      <Stepper step={current} />
      <div key={current} className="animate-fade-in">
        {current === 'mongo' && <MongoStep onDone={() => setStep('telegram')} />}
        {current === 'telegram' && <TelegramStep onDone={afterTelegram} onBack={start === 'mongo' ? () => setStep('mongo') : undefined} />}
        {current === 'password' && <PasswordStep onDone={() => setStep('done')} />}
        {current === 'done' && <DoneStep alreadyConfigured={step === null} />}
      </div>
    </AuthShell>
  );
}
