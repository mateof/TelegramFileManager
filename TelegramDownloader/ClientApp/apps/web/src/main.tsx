import { configureUiStrings } from '@tfm/ui';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import { loadLanguage, t } from './i18n';
import { startInstallCapture } from './pwa/install';
import './index.css';

// Before anything else: the browser offers installation once, early
startInstallCapture();

// The dictionary of a lazy language (Galician) arrives before anything is translated
void loadLanguage().then(() => {
  // Built-in labels of @tfm/ui, translated once (the language only changes with a reload)
  configureUiStrings({
    close: t('Close'),
    cancel: t('Cancel'),
    confirm: t('Confirm'),
    loading: t('Loading...'),
    noResults: t('No results'),
    searchPlaceholder: t('Type a command or search...'),
    breadcrumb: t('Breadcrumb'),
    more: t('More')
  });

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>
  );
});

// Service worker (production builds only): registered after the page has
// loaded, from a lazy chunk, so it costs nothing at startup.
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  const register = () => void import('./pwa/register').then((m) => m.registerPwa());
  if (document.readyState === 'complete') register();
  else window.addEventListener('load', register, { once: true });
}
