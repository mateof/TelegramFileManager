import type { DatabaseStatsDto } from '@tfm/api-client';

/**
 * Invite hash of a Telegram invite link: the part after `t.me/+`, `t.me/joinchat/`
 * or `tg://join?invite=`; a bare hash is returned as is. Null when it does not look valid.
 */
export function parseInviteHash(input: string): string | null {
  const value = input.trim();
  if (!value) return null;
  const patterns = [/(?:t(?:elegram)?\.me|telegram\.dog)\/(?:\+|joinchat\/)([\w-]+)/i, /tg:\/\/join\?invite=([\w-]+)/i];
  for (const p of patterns) {
    const m = value.match(p);
    if (m?.[1]) return m[1];
  }
  const bare = value.replace(/^\+/, '');
  return /^[\w-]{8,}$/.test(bare) ? bare : null;
}

/** A channel index whose channel the account can no longer see (left or deleted). */
export function isOrphan(db: DatabaseStatsDto): boolean {
  return /^-?\d+$/.test(db.channelId) && !db.channelName;
}

export const DIALOGS = ['create-channel', 'join-channel'] as const;
export type MaintenanceDialog = (typeof DIALOGS)[number];

export function isMaintenanceDialog(value: string | null): value is MaintenanceDialog {
  return !!value && (DIALOGS as readonly string[]).includes(value);
}
