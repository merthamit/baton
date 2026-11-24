import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { LanguageProvider, type Lang } from '../i18n/LanguageProvider';

/** Renders inside the language provider, in English unless Turkish is asked for. */
export function renderIn(ui: ReactElement, lang: Lang = 'en') {
  localStorage.setItem('baton.lang', lang);
  return render(<LanguageProvider>{ui}</LanguageProvider>);
}
