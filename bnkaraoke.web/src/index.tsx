import React from 'react';
import ReactDOM from 'react-dom/client';
import './index.css';
import App from './App';
import reportWebVitals from './reportWebVitals';
import * as serviceWorkerRegistration from './serviceWorkerRegistration';
import { installMobileClickGuard } from './utils/mobileClickGuard';

// Type-safe handler for the PWA beforeinstallprompt event
interface BeforeInstallPromptEvent extends Event {
  readonly platforms?: string[];
  prompt: () => Promise<void>;
  userChoice?: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
}

window.addEventListener('beforeinstallprompt', (e: Event) => {
  const evt = e as BeforeInstallPromptEvent;
  e.preventDefault();
  if (typeof evt.prompt === 'function') {
    void evt.prompt();
  }
});

const root = ReactDOM.createRoot(
  document.getElementById('root') as HTMLElement
);
root.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);

serviceWorkerRegistration.register();

// Install a small global guard to prevent accidental taps while scrolling (mobile only)
installMobileClickGuard({ moveThreshold: 10, timeWindowMs: 250 });

reportWebVitals();
