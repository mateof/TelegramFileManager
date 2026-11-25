import type { ApiLibraryFileDto, ApiLibraryItemDto, ApiProviderCandidateDto, LibraryKind } from '@tfm/api-client';
import { useMutation } from '@tanstack/react-query';
import { Button, Dialog, DialogContent, Input, SegmentedControl, toast } from '@tfm/ui';
import { EyeOff } from 'lucide-react';
import { useState } from 'react';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import { seriesFolderOf } from '../libraryModel';
import { useFollowScan, useInvalidateLibrary } from '../queries';
import { CandidateSearch } from './CandidateSearch';
import { SeriesFolder } from './SeriesFolder';

export type IdentifyTarget =
  | { type: 'file'; file: ApiLibraryFileDto }
  | { type: 'item'; item: ApiLibraryItemDto }
  | { type: 'folder'; channelId: number | string; path: string; name: string };

type Scope = 'file' | 'folder';

export interface IdentifyDialogProps {
  target: IdentifyTarget | null;
  onClose: () => void;
}

/** Fix an identification: search the providers and assign a file (match), a whole item (identify) or a folder (series). */
export function IdentifyDialog({ target, onClose }: IdentifyDialogProps) {
  return (
    <Dialog open={!!target} onOpenChange={(open) => !open && onClose()}>
      {target && (
        <DialogContent size="lg" title={dialogTitle(target)} description={dialogDescription(target)}>
          <IdentifyBody key={targetKey(target)} target={target} onDone={onClose} />
        </DialogContent>
      )}
    </Dialog>
  );
}

function dialogTitle(target: IdentifyTarget): string {
  if (target.type === 'file') return t('Identify file');
  return target.type === 'item' ? t('Identify title') : t('Identify folder as a series');
}

function dialogDescription(target: IdentifyTarget): string {
  if (target.type === 'file') return target.file.fileName;
  return target.type === 'item' ? t('Every file of "{title}" moves to the title you choose.', { title: target.item.title }) : target.name;
}

function targetKey(target: IdentifyTarget): string {
  if (target.type === 'file') return `${target.file.channelId}:${target.file.fileId}`;
  return target.type === 'item' ? target.item.id : `${target.channelId}:${target.path}`;
}

function initial(target: IdentifyTarget) {
  if (target.type === 'item') {
    return { q: target.item.title, kind: (target.item.kind === 'series' ? 'series' : 'movie') as LibraryKind, year: target.item.year ? String(target.item.year) : '', season: '', episode: '' };
  }
  if (target.type === 'folder') return { q: target.name, kind: 'series' as LibraryKind, year: '', season: '', episode: '' };
  const f = target.file;
  const isSeries = f.kind === 'series' || f.parsed?.isSeries;
  return {
    q: f.item?.title ?? f.parsed?.title ?? f.fileName,
    kind: (isSeries ? 'series' : 'movie') as LibraryKind,
    year: f.parsed?.year ? String(f.parsed.year) : '',
    season: String(f.season ?? f.parsed?.season ?? (isSeries ? 1 : '')),
    episode: String(f.episode ?? f.parsed?.episode ?? '')
  };
}

/** The folder a target can assign, when it has one. */
function folderOf(target: IdentifyTarget) {
  if (target.type === 'folder') return { channelId: target.channelId, folderPath: target.path, path: target.path };
  if (target.type === 'file') return { channelId: target.file.channelId, folderPath: target.file.folderPath, path: seriesFolderOf(target.file.folderPath) };
  return null;
}

function IdentifyBody({ target, onDone }: { target: IdentifyTarget; onDone: () => void }) {
  const [start] = useState(() => initial(target));
  const [kind, setKind] = useState(start.kind);
  const [season, setSeason] = useState(start.season);
  const [episode, setEpisode] = useState(start.episode);
  const [scope, setScope] = useState<Scope>(target.type === 'folder' ? 'folder' : 'file');
  const folder = folderOf(target);
  const [path, setPath] = useState(folder?.path ?? '/');
  const [includeLocked, setIncludeLocked] = useState(false);
  const invalidate = useInvalidateLibrary();
  const followScan = useFollowScan();
  const isFile = target.type === 'file';
  const assigned = folder && kind === 'series' && scope === 'folder' ? folder : null;
  const needsEpisode = isFile && kind === 'series' && !assigned;

  const choose = useMutation({
    mutationFn: async (c: ApiProviderCandidateDto) => {
      if (assigned) {
        await api.library.assignFolder({ channelId: Number(assigned.channelId), path, provider: c.provider, providerId: c.providerId, includeLocked });
      } else if (target.type === 'file') {
        if (needsEpisode && (!season || !episode)) throw new Error(t('Enter the season and the episode to assign a series file'));
        await api.library.matchFile(target.file.channelId, target.file.fileId, {
          provider: c.provider,
          providerId: c.providerId,
          kind,
          season: needsEpisode ? Number(season) : null,
          episode: needsEpisode ? Number(episode) : null
        });
      } else if (target.type === 'item') {
        await api.library.identifyItem(target.item.id, { provider: c.provider, providerId: c.providerId, kind });
      }
    },
    onSuccess: (_d, c) => {
      if (assigned) {
        toast.success(t('Folder assigned to {title}', { title: c.title }), { description: t('Its files are being identified.') });
        followScan();
      } else {
        toast.success(t('Identified as {title}', { title: c.title }));
      }
      void invalidate();
      onDone();
    },
    onError: (error) => toast.error(t('Could not save the identification'), { description: errorMessage(error) })
  });

  const ignore = useMutation({
    mutationFn: () => (target.type === 'file' ? api.library.ignoreFile(target.file.channelId, target.file.fileId) : Promise.resolve(null)),
    onSuccess: () => {
      toast.success(t('File ignored'));
      void invalidate();
      onDone();
    },
    onError: (error) => toast.error(t('Could not ignore the file'), { description: errorMessage(error) })
  });

  const digits = (v: string) => v.replace(/\D/g, '');

  return (
    <div className="flex flex-col gap-4">
      <CandidateSearch
        initialTitle={start.q}
        initialYear={start.year}
        kind={kind}
        onKindChange={target.type === 'folder' ? undefined : setKind}
        disabled={choose.isPending}
        onChoose={(c) => choose.mutate(c)}
      >
        {isFile && kind === 'series' && (
          <SegmentedControl<Scope>
            aria-label={t('What to identify')}
            value={scope}
            onValueChange={setScope}
            options={[
              { value: 'file', label: t('This file') },
              { value: 'folder', label: t('The whole folder') }
            ]}
          />
        )}
        {needsEpisode && (
          <div className="flex items-center gap-2 rounded-xl border border-border bg-surface-2/50 p-2.5 text-sm text-fg-muted">
            <span className="flex-1">{t('Episode of this file')}</span>
            <Input className="w-20" value={season} onChange={(e) => setSeason(digits(e.target.value))} aria-label={t('Season')} placeholder={t('Season')} inputMode="numeric" />
            <Input className="w-20" value={episode} onChange={(e) => setEpisode(digits(e.target.value))} aria-label={t('Episode')} placeholder={t('Episode')} inputMode="numeric" />
          </div>
        )}
        {assigned && (
          <SeriesFolder
            channelId={assigned.channelId}
            folderPath={assigned.folderPath}
            path={path}
            onPathChange={setPath}
            includeLocked={includeLocked}
            onIncludeLockedChange={setIncludeLocked}
          />
        )}
      </CandidateSearch>

      {isFile && !assigned && (
        <div className="flex justify-between gap-2">
          <Button variant="ghost" icon={<EyeOff />} loading={ignore.isPending} onClick={() => ignore.mutate()}>
            {t('Ignore this file')}
          </Button>
          <span className="self-center text-xs text-fg-subtle">{t('Manual identifications are locked: scans leave them alone.')}</span>
        </div>
      )}
    </div>
  );
}
