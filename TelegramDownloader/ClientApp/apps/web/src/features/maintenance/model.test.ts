import { describe, expect, it } from 'vitest';
import { isOrphan, parseInviteHash } from './model';

describe('parseInviteHash', () => {
  it('extracts the hash of every invite link format', () => {
    expect(parseInviteHash('https://t.me/+AbCdEf123_-x')).toBe('AbCdEf123_-x');
    expect(parseInviteHash('t.me/joinchat/AAAAAEHbEkejzxUjAUCzYA')).toBe('AAAAAEHbEkejzxUjAUCzYA');
    expect(parseInviteHash('https://telegram.me/joinchat/Zz12345678')).toBe('Zz12345678');
    expect(parseInviteHash('tg://join?invite=QwErTy1234')).toBe('QwErTy1234');
    expect(parseInviteHash('  +QwErTy1234  ')).toBe('QwErTy1234');
  });

  it('rejects things that are not invites', () => {
    expect(parseInviteHash('')).toBeNull();
    expect(parseInviteHash('https://t.me/somechannel')).toBeNull();
    expect(parseInviteHash('abc')).toBeNull();
  });
});

describe('isOrphan', () => {
  const db = { channelId: '123', channelName: null, sizeInBytes: 0, sizeText: '0 B', documentCount: 0, createdAt: null, lastModified: null };
  it('flags numeric databases without a channel name', () => {
    expect(isOrphan(db)).toBe(true);
    expect(isOrphan({ ...db, channelName: 'Music' })).toBe(false);
    expect(isOrphan({ ...db, channelId: 'TCCONFIG' })).toBe(false);
  });
});
