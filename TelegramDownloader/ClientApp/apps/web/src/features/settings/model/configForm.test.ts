import { describe, expect, it } from 'vitest';
import { searchSettings } from '../catalog';
import {
  adjustedFields,
  applyPatch,
  buildPatch,
  dirtyKeys,
  generateApiKey,
  maxMemorySplitGb,
  normalizeFolderPath,
  setDraftField,
  toFormValues,
  validate,
  validateApiKey,
  validateNewPassword
} from './configForm';
import { sampleConfig } from './fixtures';

describe('settings form', () => {
  const base = toFormValues(sampleConfig());

  it('drops read-only fields and starts write-only fields as "keep"', () => {
    expect(base).not.toHaveProperty('mobileApiKey');
    expect(base).not.toHaveProperty('hiddenChannels');
    expect(base.webDavPassword).toBeNull();
    expect(base.libraryProviders).toEqual([{ id: 'tmdb', enabled: true, priority: 1, apiKey: null }]);
  });

  it('removes a field from the draft when it goes back to the saved value', () => {
    let draft = setDraftField({}, base, 'parallelTransfers', 8);
    expect(dirtyKeys(base, draft)).toEqual(['parallelTransfers']);
    draft = setDraftField(draft, base, 'parallelTransfers', 4);
    expect(draft).toEqual({});
    expect(dirtyKeys(base, draft)).toEqual([]);
  });

  it('sends only the changed fields, trimmed', () => {
    const draft = { parallelTransfers: 8, libraryLanguage: ' en-US ', checkHash: false };
    expect(buildPatch(base, draft)).toEqual({ parallelTransfers: 8, libraryLanguage: 'en-US' });
  });

  it('sends a provider key only when one was typed or cleared', () => {
    const keep = buildPatch(base, { libraryProviders: [{ id: 'tmdb', enabled: false, priority: 1, apiKey: null }] });
    expect(keep).toEqual({ libraryProviders: [{ id: 'tmdb', enabled: false, priority: 1 }] });
    const clear = buildPatch(base, { libraryProviders: [{ id: 'tmdb', enabled: true, priority: 1, apiKey: '' }] });
    expect(clear).toEqual({ libraryProviders: [{ id: 'tmdb', enabled: true, priority: 1, apiKey: '' }] });
  });

  it('treats the WebDAV password as dirty only when set or cleared', () => {
    expect(dirtyKeys(base, { webDavPassword: null })).toEqual([]);
    expect(buildPatch(base, { webDavPassword: '' })).toEqual({ webDavPassword: '' });
    expect(buildPatch(base, { webDavPassword: 's3cret' })).toEqual({ webDavPassword: 's3cret' });
  });

  it('applies a patch optimistically, masking secrets', () => {
    const next = applyPatch(sampleConfig(), { parallelTransfers: 8, webDavPassword: '', mobileApiKey: 'abcdefghijkl', libraryProviders: [{ id: 'tmdb', apiKey: 'zzzzzzzzzz9999' }] });
    expect(next.parallelTransfers).toBe(8);
    expect(next.webDavPasswordSet).toBe(false);
    expect(next.mobileApiKey).toBe('****ijkl');
    expect(next.libraryProviders[0]).toMatchObject({ hasKey: true, apiKeyMasked: '****9999' });
  });

  it('reports the fields the server clamped', () => {
    expect(adjustedFields({ parallelTransfers: 40, checkHash: true }, sampleConfig({ parallelTransfers: 16 }))).toEqual(['parallelTransfers']);
  });
});

describe('settings validation', () => {
  const ok = toFormValues(sampleConfig());

  it('accepts the sample config', () => {
    expect(validate(ok, { isPremium: false })).toEqual({});
  });

  it('checks the numeric ranges', () => {
    const e = validate({ ...ok, maxSimultaneousDownloads: 5, parallelTransfers: 0, maxImageUploadSizeInMb: 2.5 }, { isPremium: false });
    expect(e.maxSimultaneousDownloads).toBe('Must be between 1 and 3');
    expect(e.parallelTransfers).toBe('Must be between 1 and 16');
    expect(e.maxImageUploadSizeInMb).toBe('Enter a whole number');
  });

  it('limits the split size to the Telegram limit of the account', () => {
    expect(validate({ ...ok, splitSize: 4 }, { isPremium: false }).splitSize).toBeDefined();
    expect(validate({ ...ok, splitSize: 4 }, { isPremium: true }).splitSize).toBeUndefined();
  });

  it('requires a split size for the memory split and caps its chunk', () => {
    expect(validate({ ...ok, splitSize: 0, enableMemorySplitUpload: true }, { isPremium: false }).enableMemorySplitUpload).toBe('Requires a split size above 0');
    expect(validate({ ...ok, splitSize: 2, enableMemorySplitUpload: true, memorySplitSizeGB: 3 }, { isPremium: true }).memorySplitSizeGB).toBe('Must be between 1 and 2');
    expect(maxMemorySplitGb(4, true)).toBe(4);
    expect(maxMemorySplitGb(4, false)).toBe(2);
  });

  it('validates multi-connection fields only when enabled', () => {
    const bad = { ...ok, downloadConnections: 1, multiConnectionPartSizeKB: 300 };
    expect(validate(bad, { isPremium: false })).toEqual({});
    const e = validate({ ...bad, enableMultiConnectionDownloads: true }, { isPremium: false });
    expect(e.downloadConnections).toBeDefined();
    expect(e.multiConnectionPartSizeKB).toBe('Choose one of the listed sizes');
  });

  it('validates the library fields', () => {
    const e = validate(
      {
        ...ok,
        libraryLanguage: 'spanish',
        libraryWatchedThreshold: 0.3,
        libraryFolderRules: [
          { channelId: 1, path: '/Series/', kind: 'series' },
          { channelId: 1, path: 'Series', kind: 'movie' }
        ]
      },
      { isPremium: false }
    );
    expect(e.libraryLanguage).toBeDefined();
    expect(e.libraryWatchedThreshold).toBe('Must be between 0.5 and 1');
    expect(e.libraryFolderRules).toBe('Two rules point to the same folder');
  });

  it('needs a WebDAV user for a WebDAV password', () => {
    expect(validate({ ...ok, webDavUser: '', webDavPassword: 'x' }, { isPremium: false }).webDavPassword).toBe('Set a WebDAV user too');
  });

  it('normalises folder paths', () => {
    expect(normalizeFolderPath('')).toBe('/');
    expect(normalizeFolderPath('Series')).toBe('/Series/');
    expect(normalizeFolderPath('\\Cine\\HD\\')).toBe('/Cine/HD/');
  });

  it('validates passwords and API keys', () => {
    expect(validateNewPassword('short', 'short')).toBe('Use at least 8 characters');
    expect(validateNewPassword('longenough', 'different')).toBe('The passwords do not match');
    expect(validateNewPassword('longenough', 'longenough')).toBeNull();
    expect(validateApiKey('1234567')).toBe('Use at least 8 characters');
    expect(validateApiKey('has a space')).toBe('The key cannot contain spaces');
    expect(validateApiKey(generateApiKey())).toBeNull();
    expect(generateApiKey()).toHaveLength(32);
  });
});

describe('settings search', () => {
  it('finds settings by label, help text and keywords, ignoring accents', () => {
    expect(searchSettings('webdav').map((s) => s.id)).toEqual(expect.arrayContaining(['webDavUser', 'webDavPassword', 'webDavUrl']));
    expect(searchSettings('ffmpeg').map((s) => s.id)).toContain('enableVideoTranscoding');
    expect(searchSettings('ESPAÑOL').map((s) => s.id)).toContain('language');
    expect(searchSettings('   ')).toEqual([]);
  });
});
