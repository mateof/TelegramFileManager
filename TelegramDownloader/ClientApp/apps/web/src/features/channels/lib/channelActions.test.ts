import type { ApiFileDto } from '@tfm/api-client';
import type { FileEntry } from '@tfm/file-manager';
import { describe, expect, it, vi } from 'vitest';
import { channelActions, messageIdOf } from './channelActions';

const entry = (data: Partial<ApiFileDto>, kind: FileEntry['kind'] = 'file') => ({ id: '1', name: 'a', kind, path: '/a', parentPath: '/', data }) as FileEntry;

describe('go to the message of a file', () => {
  it('uses the message of the file, or of its first part', () => {
    expect(messageIdOf(entry({ messageId: 7, firstMessageId: 7 }))).toBe(7);
    expect(messageIdOf(entry({ messageId: null, firstMessageId: 8 }))).toBe(8);
    expect(messageIdOf(entry({ messageId: 7 }))).toBe(7);
    expect(messageIdOf(entry({ messageId: null }))).toBeNull();
    expect(messageIdOf(entry({ messageId: 7 }, 'folder'))).toBeNull();
  });

  it('is offered in the channel, not in a shared collection', () => {
    const openMessage = vi.fn();
    const find = (collectionId?: string) => channelActions({ channelId: '1', channelName: 'c', collectionId, openDialog: vi.fn(), openMessage }).find((a) => a.id === 'tfm:go-to-message');
    const action = find()!;
    const file = entry({ messageId: 7 });
    expect(action.when?.([file], {} as never)).toBe(true);
    expect(action.when?.([entry({}, 'folder')], {} as never)).toBe(false);
    void action.run([file], {} as never);
    expect(openMessage).toHaveBeenCalledWith(7);
    expect(find('x')).toBeUndefined();
  });
});
