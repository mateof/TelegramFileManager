import { t } from '@/i18n';

export const passkeysQueryKey = ['settings', 'passkeys'] as const;

/** A default name for a new passkey, from the device the user is on. */
export function suggestPasskeyName(userAgent: string = typeof navigator === 'undefined' ? '' : navigator.userAgent): string {
  const ua = userAgent.toLowerCase();
  const os = ua.includes('android') ? 'Android' : /iphone|ipad/.test(ua) ? 'iOS' : ua.includes('mac os') ? 'macOS' : ua.includes('windows') ? 'Windows' : ua.includes('linux') ? 'Linux' : '';
  const browser = ua.includes('edg/') ? 'Edge' : ua.includes('firefox/') ? 'Firefox' : ua.includes('chrome/') ? 'Chrome' : ua.includes('safari/') ? 'Safari' : '';
  return [t('Passkey'), [browser, os].filter(Boolean).join(' / ')].filter(Boolean).join(' · ');
}
