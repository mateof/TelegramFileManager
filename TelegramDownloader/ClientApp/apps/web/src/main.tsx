import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import { startInstallCapture } from './pwa/install';
import './index.css';

// Before anything else: the browser offers installation once, early
startInstallCapture();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>
);

// Service worker (production builds only): registered after the page has
// loaded, from a lazy chunk, so it costs nothing at startup.
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  const register = () => void import('./pwa/register').then((m) => m.registerPwa());
  if (document.readyState === 'complete') register();
  else window.addEventListener('load', register, { once: true });
}
