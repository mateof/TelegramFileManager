import type { InvitationInfo } from '@tfm/api-client';
import { Button, Checkbox, confirm, Dialog, DialogContent, Field, Input, Textarea, toast } from '@tfm/ui';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { DoorOpen, Link2, LogIn, Plus, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { ChannelSinglePicker } from '@/features/settings/components/ChannelPicker';
import { channelName, useChannelOptions } from '@/features/settings/components/channelOptions';
import { CopyField } from '@/features/settings/components/Copy';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { queryKeys } from '@/lib/queryKeys';
import { parseInviteHash } from '../model';

interface DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

function useInvalidateChannels() {
  const qc = useQueryClient();
  return () => {
    void qc.invalidateQueries({ queryKey: queryKeys.channels });
    void qc.invalidateQueries({ queryKey: queryKeys.channelFolders });
    void qc.invalidateQueries({ queryKey: ['maintenance'] });
  };
}

export function CreateChannelDialog({ open, onOpenChange }: DialogProps) {
  const [title, setTitle] = useState('');
  const [about, setAbout] = useState('');
  const [index, setIndex] = useState(true);
  const invalidate = useInvalidateChannels();
  const create = useMutation({
    mutationFn: () => api.channels.create({ title: title.trim(), about: about.trim() || null, createDatabase: index }),
    onSuccess: (channel) => {
      toast.success(t('Channel "{name}" created', { name: channel.name }));
      invalidate();
      setTitle('');
      setAbout('');
      onOpenChange(false);
    }
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (title.trim()) create.mutate();
  };
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={t('Create channel')} description={t('A new private Telegram channel to store files.')}>
        <form onSubmit={submit} className="flex flex-col gap-4">
          <Field label={t('Name')}>{({ id }) => <Input id={id} value={title} onChange={(e) => setTitle(e.target.value)} autoFocus maxLength={128} />}</Field>
          <Field label={t('Description (optional)')}>{({ id }) => <Textarea id={id} value={about} onChange={(e) => setAbout(e.target.value)} rows={3} maxLength={255} />}</Field>
          <Checkbox label={t('Create the local index too')} checked={index} onCheckedChange={(v) => setIndex(v === true)} />
          {create.isError && <p className="text-sm text-danger">{errorMessage(create.error)}</p>}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={() => onOpenChange(false)}>
              {t('Cancel')}
            </Button>
            <Button type="submit" icon={<Plus />} loading={create.isPending} disabled={!title.trim()}>
              {t('Create')}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function JoinChannelDialog({ open, onOpenChange }: DialogProps) {
  const [link, setLink] = useState('');
  const [touched, setTouched] = useState(false);
  const hash = parseInviteHash(link);
  const invalidate = useInvalidateChannels();
  const join = useMutation({
    mutationFn: (h: string) => api.channels.join(h),
    onSuccess: () => {
      toast.success(t('Joined the channel'));
      invalidate();
      setLink('');
      setTouched(false);
      onOpenChange(false);
    }
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (hash) join.mutate(hash);
  };
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={t('Join channel')} description={t('Paste an invite link such as https://t.me/+AbCdEf or https://t.me/joinchat/AbCdEf.')}>
        <form onSubmit={submit} className="flex flex-col gap-4">
          <Field label={t('Invite link')} error={touched && !hash ? t('This does not look like an invite link') : join.isError ? errorMessage(join.error) : undefined}>
            {({ id, describedBy, invalid }) => (
              <Input id={id} value={link} onChange={(e) => setLink(e.target.value)} autoFocus leading={<Link2 />} placeholder="https://t.me/+…" aria-describedby={describedBy} aria-invalid={invalid || undefined} />
            )}
          </Field>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={() => onOpenChange(false)}>
              {t('Cancel')}
            </Button>
            <Button type="submit" icon={<LogIn />} loading={join.isPending} disabled={!link.trim()}>
              {t('Join')}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function InvitationDialog({ open, onOpenChange }: DialogProps) {
  const { options } = useChannelOptions();
  const [channel, setChannel] = useState<number | null>(null);
  const [info, setInfo] = useState<InvitationInfo | null>(null);
  const get = useMutation({ mutationFn: (id: number) => api.channels.invitation(id), onSuccess: setInfo });
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={t('Get invitation link')} description={t('Generates (or reads) the invite link of a channel you manage.')}>
        <div className="flex flex-col gap-4">
          <ChannelSinglePicker
            options={options}
            value={channel}
            onChange={(id) => {
              setChannel(id);
              setInfo(null);
              get.mutate(id);
            }}
            placeholder={t('Pick a channel')}
          />
          {get.isPending && <p className="text-sm text-fg-muted">{t('Loading...')}</p>}
          {get.isError && <p className="text-sm text-danger">{errorMessage(get.error)}</p>}
          {info && (info.invitationLink ? <CopyField value={info.invitationLink} label={t('Invite link')} /> : <p className="text-sm text-fg-muted">{t('This channel has no invite link (you may not have the rights to create one).')}</p>)}
        </div>
      </DialogContent>
    </Dialog>
  );
}

export function LeaveChannelDialog({ open, onOpenChange }: DialogProps) {
  const { options, data } = useChannelOptions();
  const [channel, setChannel] = useState<number | null>(null);
  const [deleteIndex, setDeleteIndex] = useState(true);
  const [deleteOnTelegram, setDeleteOnTelegram] = useState(false);
  const invalidate = useInvalidateChannels();
  const owner = channel !== null && (data ?? []).find((c) => c.id === channel)?.isOwner;
  const leave = useMutation({
    mutationFn: (id: number) => api.channels.leave(id, { deleteLocalDatabase: deleteIndex, deleteOnTelegram: deleteOnTelegram && !!owner }),
    onSuccess: () => {
      toast.success(deleteOnTelegram ? t('Channel deleted') : t('You left the channel'));
      invalidate();
      setChannel(null);
      onOpenChange(false);
    },
    onError: (error) => toast.error(t('Could not leave the channel'), { description: errorMessage(error) })
  });
  const run = async () => {
    if (channel === null) return;
    const name = channelName(options, channel);
    const ok = await confirm({
      title: deleteOnTelegram ? t('Delete "{name}" on Telegram?', { name }) : t('Leave "{name}"?', { name }),
      description: deleteOnTelegram ? t('The channel and every file stored in it are deleted for every member. This cannot be undone.') : t('You can join again later with an invite link.'),
      destructive: true,
      confirmLabel: deleteOnTelegram ? t('Delete channel') : t('Leave')
    });
    if (ok) leave.mutate(channel);
  };
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t('Leave or delete a channel')}
        footer={
          <>
            <Button variant="secondary" onClick={() => onOpenChange(false)}>
              {t('Cancel')}
            </Button>
            <Button variant="danger" icon={deleteOnTelegram ? <Trash2 /> : <DoorOpen />} disabled={channel === null} loading={leave.isPending} onClick={() => void run()}>
              {deleteOnTelegram ? t('Delete channel') : t('Leave')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <ChannelSinglePicker options={options} value={channel} onChange={setChannel} placeholder={t('Pick a channel')} />
          <Checkbox label={t('Also delete the local index')} checked={deleteIndex} onCheckedChange={(v) => setDeleteIndex(v === true)} />
          <Checkbox label={t('Delete the channel on Telegram (owner only)')} checked={deleteOnTelegram} disabled={!owner} onCheckedChange={(v) => setDeleteOnTelegram(v === true)} />
          {channel !== null && !owner && <p className="text-xs text-fg-muted">{t('You are not the owner of this channel: you can only leave it.')}</p>}
        </div>
      </DialogContent>
    </Dialog>
  );
}
