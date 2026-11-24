import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import '@fontsource/atkinson-hyperlegible/400.css';
import '@fontsource/atkinson-hyperlegible/700.css';
import '@fontsource/ibm-plex-mono/400.css';
import '@fontsource/ibm-plex-mono/600.css';
import './styles/tokens.css';
import { App } from './App';
import { LanguageProvider } from './i18n/LanguageProvider';
import { readTheme } from './ui/ThemeToggle';

// The saved theme before the first paint, so the page never flashes the other one.
document.documentElement.dataset.theme = readTheme();

const root = document.getElementById('root');
if (!root) {
  throw new Error('#root is missing from index.html');
}

createRoot(root).render(
  <StrictMode>
    <LanguageProvider>
      <App />
    </LanguageProvider>
  </StrictMode>,
);
