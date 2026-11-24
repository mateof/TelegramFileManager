import { toast } from '@tfm/ui';
import { t } from '@/i18n';
import { promptInstall, type InstallMode } from './install';

/** "Install app" action of the menus: the browser dialog, or the iOS instructions. */
export async function installApp(mode: InstallMode): Promise<void> {
  if (mode === 'ios') {
    toast(t('Install app'), { description: t('On iPhone and iPad: tap Share, then Add to Home Screen.'), duration: 10_000 });
    return;
  }
  const outcome = await promptInstall();
  if (outcome === 'accepted') toast.success(t('App installed'));
}
