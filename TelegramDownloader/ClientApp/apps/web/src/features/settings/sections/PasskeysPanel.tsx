import { ApiErrorCodes, isApiError, type WebPasskeyDto, type WebPasskeyListDto } from '@tfm/api-client';
import { Badge, Button, confirm, Dialog, DialogContent, Field, IconButton, Input, Skeleton, Textarea, toast } from '@tfm/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Cloud, FingerprintPattern, KeyRound, Pencil, Plus, Settings2, Trash2, TriangleAlert } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { passkeyErrorMessage } from '@/auth/passkeyMessages';
import { getLanguage, t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { formatRelative } from '@/lib/format';
import { queryKeys } from '@/lib/queryKeys';
import { createPasskey, passkeySupport } from '@/lib/webauthn';
import { anchorOf } from '../catalog';
import { SettingsGroup } from '../components/SettingRow';
import { passkeysQueryKey, suggestPasskeyName } from '../model/passkeys';

function useInvalidate() {
  const qc = useQueryClient();
  return () => {
    void qc.invalidateQueries({ queryKey: passkeysQueryKey });
    void qc.invalidateQueries({ queryKey: queryKeys.webAuthStatus });
  };
}

/** "Add passkey": name + current password → begin → browser → finish. */
function AddPasskeyDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const invalidate = useInvalidate();
  const [name, setName] = useState(() => suggestPasskeyName());
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const add = useMutation({
    mutationFn: async () => {
      const begin = await api.webAuth.passkeys.registerBegin(password);
      const credential = await createPasskey(begin.options);
      return api.webAuth.passkeys.registerFinish({ requestId: begin.requestId, name: name.trim(), credential });
    },
    onSuccess: (passkey) => {
      toast.success(t('Passkey added'), { description: passkey?.name });
      invalidate();
      setPassword('');
      setError(null);
      onOpenChange(false);
    },
    onError: (e) => setError(passkeyErrorMessage(e, 'register'))
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!password || !name.trim() || add.isPending) return;
    setError(null);
    add.mutate();
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(o) => {
        if (!o) {
          setPassword('');
          setError(null);
        }
        onOpenChange(o);
      }}
    >
      <DialogContent
        title={t('Add passkey')}
        description={t('Your browser, password manager (Bitwarden, 1Password...), phone or security key will create a passkey for this server.')}
        footer={
          <>
            <Button variant="secondary" onClick={() => onOpenChange(false)}>
              {t('Cancel')}
            </Button>
            <Button type="submit" form="add-passkey-form" icon={<FingerprintPattern />} loading={add.isPending} disabled={!password || !name.trim()}>
              {t('Create passkey')}
            </Button>
          </>
        }
      >
        <form id="add-passkey-form" onSubmit={submit} className="flex flex-col gap-4" noValidate>
          <Field label={t('Name')} hint={t('To tell your passkeys apart, e.g. "Bitwarden" or "Work laptop".')}>
            {({ id, describedBy }) => <Input id={id} value={name} maxLength={64} onChange={(e) => setName(e.target.value)} aria-describedby={describedBy} />}
          </Field>
          <Field label={t('Current password')} hint={t('Required to add a passkey.')}>
            {({ id, describedBy }) => (
              <Input id={id} type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} aria-describedby={describedBy} leading={<KeyRound />} autoFocus />
            )}
          </Field>
          {error && (
            <p role="alert" className="text-sm text-danger">
              {error}
            </p>
          )}
        </form>
      </DialogContent>
    </Dialog>
  );
}

function RenamePasskeyDialog({ passkey, onClose }: { passkey: WebPasskeyDto | null; onClose: () => void }) {
  const invalidate = useInvalidate();
  const [name, setName] = useState(passkey?.name ?? '');
  const [lastId, setLastId] = useState(passkey?.id);
  if (passkey?.id !== lastId) {
    setLastId(passkey?.id);
    setName(passkey?.name ?? '');
  }
  const rename = useMutation({
    mutationFn: () => api.webAuth.passkeys.rename(passkey!.id, name.trim()),
    onSuccess: () => {
      toast.success(t('Passkey renamed'));
      invalidate();
      onClose();
    },
    onError: (e) => toast.error(t('Could not rename the passkey'), { description: errorMessage(e) })
  });
  return (
    <Dialog open={!!passkey} onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('Rename passkey')}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={onClose}>
              {t('Cancel')}
            </Button>
            <Button type="submit" form="rename-passkey-form" loading={rename.isPending} disabled={!name.trim()}>
              {t('Save')}
            </Button>
          </>
        }
      >
        <form
          id="rename-passkey-form"
          onSubmit={(e) => {
            e.preventDefault();
            if (name.trim()) rename.mutate();
          }}
        >
          <Field label={t('Name')}>{({ id }) => <Input id={id} value={name} maxLength={64} onChange={(e) => setName(e.target.value)} autoFocus />}</Field>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function PasskeyItem({ passkey, onRename }: { passkey: WebPasskeyDto; onRename: (p: WebPasskeyDto) => void }) {
  const invalidate = useInvalidate();
  const lang = getLanguage();
  const remove = useMutation({
    mutationFn: () => api.webAuth.passkeys.remove(passkey.id),
    onSuccess: () => {
      toast.success(t('Passkey removed'));
      invalidate();
    },
    onError: (e) => toast.error(t('Could not remove the passkey'), { description: errorMessage(e) })
  });
  return (
    <li className="flex items-center gap-3 px-5 py-3">
      <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-surface-2 text-accent-400 [&_svg]:size-4.5">
        <FingerprintPattern />
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="truncate text-sm font-medium text-fg">{passkey.name}</span>
          {passkey.backupEligible && (
            <Badge tone="info" title={passkey.backedUp ? t('Synced between devices and backed up') : t('Can be synced between devices')}>
              <Cloud className="size-3" />
              {t('Synced')}
            </Badge>
          )}
        </div>
        <p className="mt-0.5 text-xs text-fg-muted">
          {t('Added {when}', { when: formatRelative(passkey.createdUtc, lang) })}
          {' · '}
          {passkey.lastUsedUtc ? t('Last used {when}', { when: formatRelative(passkey.lastUsedUtc, lang) }) : t('Never used')}
        </p>
      </div>
      <div className="flex shrink-0 items-center gap-1">
        <IconButton label={t('Rename')} size="sm" onClick={() => onRename(passkey)}>
          <Pencil />
        </IconButton>
        <IconButton
          label={t('Delete')}
          size="sm"
          disabled={remove.isPending}
          onClick={async () => {
            const ok = await confirm({
              title: t('Remove the passkey "{name}"?', { name: passkey.name }),
              description: t('It will no longer sign in to this server. Delete it from your password manager or device too.'),
              destructive: true,
              confirmLabel: t('Remove')
            });
            if (ok) remove.mutate();
          }}
        >
          <Trash2 />
        </IconButton>
      </div>
    </li>
  );
}

/** Advanced: relying party override for reverse proxies that do not forward the host. */
function RelyingPartyForm({ list }: { list: WebPasskeyListDto }) {
  const invalidate = useInvalidate();
  const qc = useQueryClient();
  const [rpId, setRpId] = useState(list.rpIdOverride ?? '');
  const [origins, setOrigins] = useState(list.originsOverride.join('\n'));
  const save = useMutation({
    mutationFn: () =>
      api.webAuth.passkeys.updateSettings({
        rpId: rpId.trim() || null,
        origins: origins
          .split(/[\n,]/)
          .map((o) => o.trim())
          .filter(Boolean)
      }),
    onSuccess: (data) => {
      if (data) qc.setQueryData(passkeysQueryKey, data);
      invalidate();
      toast.success(t('Passkey domain settings saved'));
    },
    onError: (e) => toast.error(t('Could not save the settings'), { description: errorMessage(e) })
  });
  return (
    <details className="group px-5 py-4">
      <summary className="flex cursor-pointer select-none items-center gap-2 text-sm font-medium text-fg">
        <Settings2 className="size-4 text-fg-subtle" />
        {t('Advanced')}
      </summary>
      <form
        className="mt-3 flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        <p className="text-xs leading-relaxed text-fg-muted">
          {t('Only for reverse proxies that do not pass the original host name. By default the domain is the host name in the address bar and the origin is the address of this page.')}
        </p>
        <p className="flex items-start gap-1.5 text-xs font-medium text-warning">
          <TriangleAlert className="mt-0.5 size-3.5 shrink-0" />
          {t('A passkey only works on the domain it was created for: changing the domain makes the existing passkeys unusable.')}
        </p>
        <Field label={t('Domain (RP ID)')} hint={t('e.g. example.com. Empty: the host name of each request.')}>
          {({ id, describedBy }) => <Input id={id} value={rpId} placeholder={list.rpId} onChange={(e) => setRpId(e.target.value)} aria-describedby={describedBy} />}
        </Field>
        <Field label={t('Allowed origins')} hint={t('One per line, e.g. https://tfm.example.com. They must be on the domain above. Empty: the address of each request.')}>
          {({ id, describedBy }) => <Textarea id={id} value={origins} rows={3} placeholder={list.origins.join('\n')} onChange={(e) => setOrigins(e.target.value)} aria-describedby={describedBy} />}
        </Field>
        <div>
          <Button type="submit" variant="outline" size="sm" loading={save.isPending}>
            {t('Save')}
          </Button>
        </div>
      </form>
    </details>
  );
}

/** Passkeys of the Web security section. Shown only while a password is set. */
export function PasskeysPanel() {
  const support = passkeySupport();
  const [adding, setAdding] = useState(false);
  const [renaming, setRenaming] = useState<WebPasskeyDto | null>(null);
  const list = useQuery({ queryKey: passkeysQueryKey, queryFn: ({ signal }) => api.webAuth.passkeys.list(signal), retry: false });
  const noSession = isApiError(list.error) && list.error.code === ApiErrorCodes.SessionRequired;
  const data = list.data;
  const canAdd = support === 'ok' && !!data?.supported;

  return (
    <SettingsGroup
      title={t('Passkeys')}
      description={t('Sign in without typing the password, with Bitwarden, Windows Hello, Touch ID, your phone or a security key. The password keeps working.')}
      icon={<FingerprintPattern />}
      actions={
        data && (
          <Button size="sm" icon={<Plus />} disabled={!canAdd} onClick={() => setAdding(true)}>
            {t('Add passkey')}
          </Button>
        )
      }
    >
      <div id={anchorOf('passkeys')} className="scroll-mt-24 flex flex-col gap-1.5 px-5 py-4 text-xs leading-relaxed text-fg-muted">
        {support !== 'ok' && (
          <p className="flex items-start gap-1.5 font-medium text-warning" role="note">
            <TriangleAlert className="mt-0.5 size-3.5 shrink-0" />
            {support === 'insecure'
              ? t('Passkeys need HTTPS (or localhost). A passkey only works on the domain where it was created.')
              : t('This browser does not support passkeys.')}
          </p>
        )}
        {data && !data.supported && (
          <p className="flex items-start gap-1.5 font-medium text-warning" role="note">
            <TriangleAlert className="mt-0.5 size-3.5 shrink-0" />
            {t('Passkeys do not work when the server is opened by IP address. Open it by its host name (localhost works).')}
          </p>
        )}
        {data && (
          <p>
            {t('A passkey only works on the domain where it was created.')} {t('Domain')}: <code className="font-mono text-fg">{data.rpId}</code>
          </p>
        )}
      </div>

      {list.isPending ? (
        <div className="px-5 py-4">
          <Skeleton className="h-14" />
        </div>
      ) : noSession ? (
        <p className="px-5 py-4 text-sm text-fg-muted">{t('Sign in with the password in this browser to manage passkeys.')}</p>
      ) : list.isError ? (
        <p className="px-5 py-4 text-sm text-danger">{errorMessage(list.error)}</p>
      ) : data!.items.length === 0 ? (
        <p className="px-5 py-4 text-sm text-fg-muted">{t('No passkeys yet.')}</p>
      ) : (
        <ul className="divide-y divide-border" aria-label={t('Passkeys')}>
          {data!.items.map((p) => (
            <PasskeyItem key={p.id} passkey={p} onRename={setRenaming} />
          ))}
        </ul>
      )}

      {data && <RelyingPartyForm list={data} />}
      {adding && <AddPasskeyDialog open={adding} onOpenChange={setAdding} />}
      <RenamePasskeyDialog passkey={renaming} onClose={() => setRenaming(null)} />
    </SettingsGroup>
  );
}
