import { useCallback, useEffect, useState } from 'react';
import { ApiError, fetchPatient, fetchWard } from './api/client';
import { useLatest } from './api/useLatest';
import { BoardSkeleton, WardBoard } from './board/WardBoard';
import { TimeControl } from './board/TimeControl';
import { LanguageToggle, useT } from './i18n/LanguageProvider';
import { PatientDetail } from './patient/PatientDetail';
import { readUrlState, writeUrlState } from './url';
import './board/board.css';

export function App() {
  const { t, lang } = useT();
  const [view, setView] = useState(() => readUrlState(window.location.search));
  const ward = useLatest((signal) => fetchWard(view.at, lang, signal), [view.at, lang]);
  const at = view.at ?? ward.data?.at;
  const patient = useLatest(
    (signal) => (view.bed && at ? fetchPatient(view.bed, at, lang, signal) : Promise.resolve(undefined)),
    [view.bed, at, lang],
    view.bed,
  );

  useEffect(() => {
    const search = writeUrlState(view);
    if (search !== window.location.search) {
      window.history.replaceState(null, '', `${window.location.pathname}${search}`);
    }
  }, [view]);

  const selectBed = useCallback((bed: string) => {
    setView((v) => ({ ...v, bed }));
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    document.querySelector('.detail')?.scrollIntoView?.({ block: 'start', behavior: reduce ? 'auto' : 'smooth' });
  }, []);
  const setAt = useCallback((at: string) => setView((v) => ({ ...v, at })), []);
  const data = ward.data;

  return (
    <div className="wrap">
      <header className="top">
        <div className="brand">
          <svg className="mark" viewBox="0 0 24 24" width="22" height="22" aria-hidden="true" focusable="false">
            <path d="M5 19 19 5" stroke="currentColor" strokeWidth="3.2" strokeLinecap="round" />
            <circle cx="5" cy="19" r="2.6" fill="currentColor" />
          </svg>
          <h1>
            {t.wardName} <span className="muted">· {t.boardTitle}</span>
          </h1>
        </div>
        <p className="notice">
          <strong>{t.syntheticNotice}</strong>
        </p>
        <LanguageToggle />
      </header>

      {data && (
        <section className="controls" aria-label={t.timeLabel}>
          <TimeControl shiftStart={data.shiftStart} shiftEnd={data.shiftEnd} at={view.at ?? data.at} onChange={setAt} />
          <p className="hint muted">{t.timeHint}</p>
          <p className="source muted">{t.dataSource(data.source)}</p>
        </section>
      )}

      {ward.error && (
        <div className="alert" role="alert">
          <p>{ward.error instanceof ApiError && ward.error.status === 503 ? t.fhirDown : t.loadError}</p>
          <button type="button" className="btn" onClick={ward.retry}>
            {t.retry}
          </button>
        </div>
      )}

      {/* After an error the last answer belongs to another moment than the clock shows: never display it. */}
      {ward.error ? null : data ? <WardBoard ward={data} selectedId={view.bed} onSelect={selectBed} /> : <BoardSkeleton />}

      {!ward.error && (
        <section className="detail" aria-label={t.observations}>
          {patient.error ? (
            <div className="alert" role="alert">
              <p>{t.patientError}</p>
              <button type="button" className="btn" onClick={patient.retry}>
                {t.retry}
              </button>
            </div>
          ) : data && patient.data && at ? (
            <PatientDetail series={patient.data} at={at} shiftStart={data.shiftStart} shiftEnd={data.shiftEnd} />
          ) : (
            <p className="muted">{view.bed && patient.loading ? t.patientLoading : t.selectHint}</p>
          )}
        </section>
      )}

      <footer className="foot muted">{t.syntheticDetail}</footer>
    </div>
  );
}
