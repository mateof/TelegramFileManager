import type { AppConfigDto, LibraryFolderRule, UpdateConfigRequest } from '@tfm/api-client';
import { t } from '@/i18n';

/**
 * Pure logic of the settings form: what can be edited, the diff sent to
 * `PATCH /api/v1/config`, the optimistic copy of the config and validation.
 * Kept free of React so it is unit tested on its own.
 */

/** Provider settings as edited in the form. `apiKey`: null keeps the key, '' clears it. */
export interface ProviderDraft {
  id: string;
  enabled: boolean;
  priority: number;
  apiKey: string | null;
}

/** Config fields that are never edited through the Save button (read-only or immediate actions). */
type ReadOnlyKeys =
  | 'favouriteChannels'
  | 'hiddenChannels'
  | 'mobileApiKey'
  | 'mobileApiKeySet'
  | 'webDavPasswordSet'
  | 'libraryProviders';

/** Every value of the settings form. */
export type FormValues = Omit<AppConfigDto, ReadOnlyKeys> & {
  /** Write-only: null keeps the current password, '' clears it, anything else sets it. */
  webDavPassword: string | null;
  libraryProviders: ProviderDraft[];
};

export type FormKey = keyof FormValues;
export type Draft = Partial<FormValues>;
export type FormErrors = Partial<Record<FormKey, string>>;

/** Telegram per-file limit in GB (Premium raises it to 4). */
export function telegramMaxGb(isPremium: boolean): number {
  return isPremium ? 4 : 2;
}

/** Upper bound of the memory split chunk: ≤ split size and ≤ the Telegram limit. */
export function maxMemorySplitGb(splitSize: number, isPremium: boolean): number {
  return Math.max(1, Math.min(splitSize || telegramMaxGb(isPremium), telegramMaxGb(isPremium)));
}

export const CHUNK_SIZES_KB = [128, 256, 512, 1024] as const;

/** Form values of a config read from the server. */
export function toFormValues(config: AppConfigDto): FormValues {
  const {
    favouriteChannels: _f,
    hiddenChannels: _h,
    mobileApiKey: _k,
    mobileApiKeySet: _ks,
    webDavPasswordSet: _w,
    libraryProviders,
    ...rest
  } = config;
  return {
    ...rest,
    webDavPassword: null,
    libraryProviders: libraryProviders.map((p) => ({ id: p.id, enabled: p.enabled, priority: p.priority, apiKey: null }))
  };
}

export function sameValue(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (typeof a === 'object' && typeof b === 'object' && a !== null && b !== null) return JSON.stringify(a) === JSON.stringify(b);
  return false;
}

/** Puts one field into the draft, removing it again when it equals the saved value. */
export function setDraftField<K extends FormKey>(draft: Draft, base: FormValues | undefined, key: K, value: FormValues[K]): Draft {
  const next: Draft = { ...draft, [key]: value };
  if (base && sameValue(base[key], value)) delete next[key];
  return next;
}

/** Fields of the draft that really differ from the saved values. */
export function dirtyKeys(base: FormValues | undefined, draft: Draft): FormKey[] {
  if (!base) return [];
  return (Object.keys(draft) as FormKey[]).filter((k) => {
    if (k === 'webDavPassword') return draft.webDavPassword !== null && draft.webDavPassword !== undefined;
    if (k === 'libraryProviders') return (draft.libraryProviders ?? []).some((p, i) => providerChanged(base.libraryProviders[i], p));
    return !sameValue(base[k], draft[k]);
  });
}

function providerChanged(saved: ProviderDraft | undefined, p: ProviderDraft): boolean {
  if (!saved) return true;
  return saved.enabled !== p.enabled || saved.priority !== p.priority || p.apiKey !== null;
}

/** The PATCH body: only the fields that changed. */
export function buildPatch(base: FormValues, draft: Draft): UpdateConfigRequest {
  const patch: Record<string, unknown> = {};
  for (const key of dirtyKeys(base, draft)) {
    if (key === 'libraryProviders') {
      patch.libraryProviders = (draft.libraryProviders ?? [])
        .filter((p, i) => providerChanged(base.libraryProviders.find((s) => s.id === p.id) ?? base.libraryProviders[i], p))
        .map((p) => (p.apiKey === null ? { id: p.id, enabled: p.enabled, priority: p.priority } : { id: p.id, enabled: p.enabled, priority: p.priority, apiKey: p.apiKey.trim() }));
      continue;
    }
    if (key === 'libraryLanguage' || key === 'webDavUser') {
      patch[key] = String(draft[key] ?? '').trim();
      continue;
    }
    patch[key] = draft[key];
  }
  return patch as UpdateConfigRequest;
}

/** What the config looks like once the patch is applied (optimistic update). */
export function applyPatch(config: AppConfigDto, patch: UpdateConfigRequest): AppConfigDto {
  const { webDavPassword, libraryProviders, mobileApiKey, ...plain } = patch;
  const next = { ...config } as AppConfigDto & Record<string, unknown>;
  for (const [k, v] of Object.entries(plain)) if (v !== undefined && v !== null) next[k] = v;
  if (webDavPassword !== undefined && webDavPassword !== null) next.webDavPasswordSet = webDavPassword !== '';
  if (mobileApiKey !== undefined && mobileApiKey !== null) {
    next.mobileApiKeySet = mobileApiKey !== '' || config.mobileApiKeySet;
    next.mobileApiKey = mobileApiKey === '' ? null : maskSecret(mobileApiKey);
  }
  if (libraryProviders) {
    next.libraryProviders = config.libraryProviders.map((p) => {
      const change = libraryProviders.find((c) => c.id === p.id);
      if (!change) return p;
      const hasKey = change.apiKey === undefined || change.apiKey === null ? p.hasKey : change.apiKey !== '';
      return {
        ...p,
        enabled: change.enabled ?? p.enabled,
        priority: change.priority ?? p.priority,
        hasKey,
        apiKeyMasked: change.apiKey ? maskSecret(change.apiKey) : change.apiKey === '' ? null : p.apiKeyMasked
      };
    });
  }
  return next;
}

/** Same masking as the server: `****` + the last 4 characters. */
export function maskSecret(secret: string): string {
  return secret.length <= 8 ? '****' : `****${secret.slice(-4)}`;
}

/** Fields whose saved value differs from what was requested (clamped by the server). */
export function adjustedFields(patch: UpdateConfigRequest, saved: AppConfigDto): string[] {
  const out: string[] = [];
  for (const [k, v] of Object.entries(patch)) {
    if (typeof v !== 'number') continue;
    const actual = (saved as unknown as Record<string, unknown>)[k];
    if (typeof actual === 'number' && actual !== v) out.push(k);
  }
  return out;
}

const LANGUAGE_TAG = /^[a-z]{2,3}(-[A-Za-z]{2,4})?$/;

function range(errors: FormErrors, key: FormKey, value: number, min: number, max: number) {
  if (!Number.isFinite(value) || !Number.isInteger(value)) errors[key] = t('Enter a whole number');
  else if (value < min || value > max) errors[key] = t('Must be between {min} and {max}', { min, max });
}

/** Validation of the whole form; the keys of the result are the invalid fields. */
export function validate(v: FormValues, ctx: { isPremium: boolean }): FormErrors {
  const e: FormErrors = {};
  range(e, 'maxSimultaneousDownloads', v.maxSimultaneousDownloads, 1, 3);
  range(e, 'parallelTransfers', v.parallelTransfers, 1, 16);
  range(e, 'timeSleepBetweenTransactions', v.timeSleepBetweenTransactions, 0, 600_000);
  if (v.enableMultiConnectionDownloads) {
    range(e, 'downloadConnections', v.downloadConnections, 2, 8);
    if (!(CHUNK_SIZES_KB as readonly number[]).includes(v.multiConnectionPartSizeKB)) {
      e.multiConnectionPartSizeKB = t('Choose one of the listed sizes');
    }
    range(e, 'multiConnectionBlockSizeMB', v.multiConnectionBlockSizeMB, 1, 16);
    range(e, 'multiConnectionMinFileSizeMB', v.multiConnectionMinFileSizeMB, 1, 1024);
  }
  range(e, 'maxPreloadFileSizeInMb', v.maxPreloadFileSizeInMb, 0, 10_000);
  range(e, 'maxImageUploadSizeInMb', v.maxImageUploadSizeInMb, 0, 10);
  const maxGb = telegramMaxGb(ctx.isPremium);
  range(e, 'splitSize', v.splitSize, 0, maxGb);
  if (v.enableMemorySplitUpload) {
    if (v.splitSize === 0) e.enableMemorySplitUpload = t('Requires a split size above 0');
    else range(e, 'memorySplitSizeGB', v.memorySplitSizeGB, 1, maxMemorySplitGb(v.splitSize, ctx.isPremium));
  }
  if (v.enableTaskPersistence) {
    range(e, 'taskPersistenceDebounceSeconds', v.taskPersistenceDebounceSeconds, 1, 60);
    range(e, 'staleTaskCleanupDays', v.staleTaskCleanupDays, 1, 30);
  }
  if (!Number.isFinite(v.libraryWatchedThreshold) || v.libraryWatchedThreshold < 0.5 || v.libraryWatchedThreshold > 1) {
    e.libraryWatchedThreshold = t('Must be between {min} and {max}', { min: 0.5, max: 1 });
  }
  if (!LANGUAGE_TAG.test(v.libraryLanguage.trim())) e.libraryLanguage = t('Use a language tag such as es-ES or en-US');
  if (v.webDavPassword && !v.webDavUser?.trim()) e.webDavPassword = t('Set a WebDAV user too');
  if (v.libraryProviders.some((p) => !Number.isInteger(p.priority) || p.priority < 0 || p.priority > 100)) {
    e.libraryProviders = t('Priorities must be whole numbers between 0 and 100');
  }
  if (hasDuplicateRules(v.libraryFolderRules)) e.libraryFolderRules = t('Two rules point to the same folder');
  return e;
}

function hasDuplicateRules(rules: LibraryFolderRule[]): boolean {
  const seen = new Set<string>();
  for (const r of rules) {
    const k = `${r.channelId}|${normalizeFolderPath(r.path)}`;
    if (seen.has(k)) return true;
    seen.add(k);
  }
  return false;
}

/** `Series` → `/Series/`, `` → `/`. */
export function normalizeFolderPath(path: string): string {
  const trimmed = path.trim().replace(/\\/g, '/').replace(/\/+/g, '/');
  if (!trimmed || trimmed === '/') return '/';
  return `/${trimmed.replace(/^\/|\/$/g, '')}/`;
}

/** A random API key (URL-safe, 32 characters) from the browser crypto API. */
export function generateApiKey(length = 32): string {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789';
  const bytes = new Uint8Array(length);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => alphabet[b % alphabet.length]).join('');
}

/** Rules of the web password (same as the server: at least 8 characters). */
export const MIN_WEB_PASSWORD = 8;

export function validateNewPassword(next: string, repeat: string): string | null {
  if (next.length < MIN_WEB_PASSWORD) return t('Use at least {count} characters', { count: MIN_WEB_PASSWORD });
  if (next !== repeat) return t('The passwords do not match');
  return null;
}

/** Validation of a custom API key (the server requires 8+ characters). */
export function validateApiKey(key: string): string | null {
  const k = key.trim();
  if (k.length < 8) return t('Use at least {count} characters', { count: 8 });
  if (/\s/.test(k)) return t('The key cannot contain spaces');
  return null;
}
