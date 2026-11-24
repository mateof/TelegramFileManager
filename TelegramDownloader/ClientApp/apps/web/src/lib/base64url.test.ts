import { describe, expect, it } from 'vitest';
import { base64urlToBuffer, bufferToBase64url } from './base64url';

const bytes = (...values: number[]) => new Uint8Array(values);

describe('base64url', () => {
  it('encodes without padding and with the url-safe alphabet', () => {
    expect(bufferToBase64url(bytes())).toBe('');
    expect(bufferToBase64url(bytes(0xfb, 0xff))).toBe('-_8');
    expect(bufferToBase64url(bytes(0x66))).toBe('Zg');
    expect(bufferToBase64url(bytes(0x66, 0x6f))).toBe('Zm8');
    expect(bufferToBase64url(bytes(0x66, 0x6f, 0x6f))).toBe('Zm9v');
  });

  it('accepts ArrayBuffers and views (respecting offset and length)', () => {
    const buffer = bytes(1, 2, 3, 4, 5).buffer;
    expect(bufferToBase64url(buffer)).toBe('AQIDBAU');
    expect(bufferToBase64url(new Uint8Array(buffer, 1, 3))).toBe('AgME');
    expect(bufferToBase64url(new DataView(buffer, 3))).toBe('BAU');
  });

  it('round-trips random data of every length', () => {
    for (let len = 0; len < 70; len++) {
      const data = crypto.getRandomValues(new Uint8Array(len));
      const decoded = new Uint8Array(base64urlToBuffer(bufferToBase64url(data)));
      expect(decoded).toEqual(data);
    }
  });

  it('round-trips large buffers', () => {
    const data = new Uint8Array(200_000).map((_, i) => i % 251);
    expect(new Uint8Array(base64urlToBuffer(bufferToBase64url(data)))).toEqual(data);
  });

  it('decodes padded input and plain base64 too', () => {
    expect(new Uint8Array(base64urlToBuffer('Zg=='))).toEqual(bytes(0x66));
    expect(new Uint8Array(base64urlToBuffer('+/8'))).toEqual(bytes(0xfb, 0xff));
    expect(new Uint8Array(base64urlToBuffer('-_8'))).toEqual(bytes(0xfb, 0xff));
  });

  it('rejects invalid input', () => {
    expect(() => base64urlToBuffer('a')).toThrow(TypeError);
    expect(() => base64urlToBuffer('ab$c')).toThrow(TypeError);
    expect(() => base64urlToBuffer('ab=c')).toThrow(TypeError);
  });
});
