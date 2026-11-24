import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { en, type Messages } from './en';
import { tr } from './tr';

export type Lang = 'en' | 'tr';

const dictionaries: Record<Lang, Messages> = { en, tr };
const storageKey = 'baton.lang';

interface LanguageState {
  lang: Lang;
  t: Messages;
  setLang: (lang: Lang) => void;
}

const LanguageContext = createContext<LanguageState | null>(null);

function readStored(): Lang {
  try {
    return localStorage.getItem(storageKey) === 'tr' ? 'tr' : 'en';
  } catch {
    return 'en';
  }
}

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(readStored);

  useEffect(() => {
    document.documentElement.lang = lang;
  }, [lang]);

  const setLang = useCallback((next: Lang) => {
    setLangState(next);
    try {
      localStorage.setItem(storageKey, next);
    } catch {
      // storage blocked: the choice still applies to this page
    }
  }, []);

  const value = useMemo(() => ({ lang, t: dictionaries[lang], setLang }), [lang, setLang]);
  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

export function useT(): LanguageState {
  const value = useContext(LanguageContext);
  if (!value) {
    throw new Error('useT must be used inside LanguageProvider');
  }
  return value;
}

/** Two pressed-state buttons; each is labelled in its own language. */
export function LanguageToggle() {
  const { lang, t, setLang } = useT();
  return (
    <div className="seg" role="group" aria-label={t.languageLabel}>
      {(Object.keys(dictionaries) as Lang[]).map((code) => (
        <button key={code} type="button" lang={code} aria-pressed={lang === code} onClick={() => setLang(code)}>
          {dictionaries[code].langName}
        </button>
      ))}
    </div>
  );
}
