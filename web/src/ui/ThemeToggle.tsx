import { useEffect, useState } from 'react';
import { useT } from '../i18n/LanguageProvider';

export type Theme = 'light' | 'dark';

const key = 'baton.theme';

/** The saved theme; light by default (a lit ward and a shared screen read best in light), dark on request. */
export function readTheme(): Theme {
  try {
    return localStorage.getItem(key) === 'dark' ? 'dark' : 'light';
  } catch {
    return 'light';
  }
}

/** Light | Dark: sets data-theme on <html> and remembers the choice in this browser. */
export function ThemeToggle() {
  const { t } = useT();
  const [theme, setTheme] = useState<Theme>(readTheme);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try {
      localStorage.setItem(key, theme);
    } catch {
      // storage blocked: the choice lasts for this page only
    }
  }, [theme]);

  return (
    <div className="seg seg-small" role="group" aria-label={t.themeLabel}>
      <button type="button" aria-pressed={theme === 'light'} onClick={() => setTheme('light')}>
        {t.themeLight}
      </button>
      <button type="button" aria-pressed={theme === 'dark'} onClick={() => setTheme('dark')}>
        {t.themeDark}
      </button>
    </div>
  );
}
