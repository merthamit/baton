import { LanguageToggle, useT } from './i18n/LanguageProvider';

/** Shell; the board and patient detail are added in the next steps. */
export function App() {
  const { t } = useT();
  return (
    <div className="wrap">
      <header className="top">
        <div>
          <div className="lbl">
            {t.appName} · {t.boardTitle}
          </div>
          <h1>{t.wardName}</h1>
        </div>
        <p className="notice">{t.syntheticNotice}</p>
        <LanguageToggle />
      </header>
    </div>
  );
}
