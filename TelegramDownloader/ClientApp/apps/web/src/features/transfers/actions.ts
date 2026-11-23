import type { TransferClearState, TransferDto } from '@tfm/api-client';
import { confirm, toast } from '@tfm/ui';
import { t } from '@/i18n';
import { api, errorMessage } from '@/lib/api';
import type { StateGroup, TransferAction, TransferTab } from './model';

export function actionLabel(action: TransferAction): string {
  switch (action) {
    case 'pause':
      return t('Pause');
    case 'resume':
      return t('Resume');
    case 'cancel':
      return t('Cancel');
    case 'retry':
      return t('Retry');
    case 'remove':
      return t('Remove from the list');
  }
}

export function stateLabel(group: StateGroup): string {
  switch (group) {
    case 'active':
      return t('Running');
    case 'queued':
      return t('Queued');
    case 'paused':
      return t('Paused');
    case 'done':
      return t('Completed');
    case 'failed':
      return t('Failed');
    case 'canceled':
      return t('Cancelled');
  }
}

/** Run one per-item action; the new state arrives through the hub. */
export async function runAction(item: TransferDto, action: TransferAction): Promise<void> {
  try {
    if (action === 'pause') await api.transfers.pause(item.id);
    else if (action === 'resume' || action === 'retry') await api.transfers.retry(item.id);
    else if (action === 'cancel') await api.transfers.cancel(item.id);
    else await api.transfers.remove(item.id);
  } catch (e) {
    toast.error(t('Could not update "{name}"', { name: item.name }), { description: errorMessage(e) });
  }
}

export async function pauseAllDownloads(paused: boolean): Promise<void> {
  try {
    if (paused) await api.transfers.resumeDownloads();
    else await api.transfers.pauseDownloads();
  } catch (e) {
    toast.error(t('Could not update the downloads'), { description: errorMessage(e) });
  }
}

export async function stopAllDownloads(): Promise<void> {
  const ok = await confirm({
    title: t('Stop every download?'),
    description: t('The running downloads are cancelled and the queue is emptied.'),
    confirmLabel: t('Stop all'),
    destructive: true
  });
  if (!ok) return;
  try {
    await api.transfers.stopDownloads();
    toast.success(t('Downloads stopped'));
  } catch (e) {
    toast.error(t('Could not stop the downloads'), { description: errorMessage(e) });
  }
}

export async function clearFinished(tab: TransferTab, states?: TransferClearState[]): Promise<void> {
  try {
    await api.transfers.clear({ scope: tab, states });
  } catch (e) {
    toast.error(t('Could not clear the list'), { description: errorMessage(e) });
  }
}

export async function clearQueue(tab: 'downloads' | 'uploads'): Promise<void> {
  const ok = await confirm({
    title: t('Empty the queue?'),
    description: t('Queued transfers are removed; running ones keep going.'),
    confirmLabel: t('Empty queue'),
    destructive: true
  });
  if (!ok) return;
  try {
    await api.transfers.clearQueue(tab);
  } catch (e) {
    toast.error(t('Could not empty the queue'), { description: errorMessage(e) });
  }
}
