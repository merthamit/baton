import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, addTryAction, confirmHandover, endTry, fetchHandover, fetchHealth, fetchPatient, fetchWard, startTry, type TryActionKind } from './api/client';
import { useLatest } from './api/useLatest';
import { useLiveChanges } from './api/useLiveChanges';
import { LiveControl, nextLiveAt, type LiveSpeed } from './board/LiveControl';
import { BoardSkeleton, WardBoard } from './board/WardBoard';
import { TimeControl } from './board/TimeControl';
import { HandoverList } from './handover/HandoverList';
import { SbarPanel } from './handover/SbarPanel';
import { LanguageToggle, useT } from './i18n/LanguageProvider';
import { ActionPanel } from './patient/ActionPanel';
import { PatientDetail } from './patient/PatientDetail';
import type { DialogResult } from './ui/ConfirmDialog';
import { readUrlState, writeUrlState } from './url';
import './board/board.css';

/** The API's answer for a Try session it no longer has (see TryOverlay.Expired). */
function isSessionGone(e: unknown): boolean {
  return e instanceof ApiError && e.status === 404 && e.title === 'Try session expired';
}

interface TrySessionState {
  id: string;
  patientId: string;
  lastActionAt: string;
}

export function App() {
  const { t, lang } = useT();
  const [view, setView] = useState(() => readUrlState(window.location.search));
  const [trySession, setTrySession] = useState<TrySessionState | null>(null);
  const [revision, setRevision] = useState(0);
  const [tryMessage, setTryMessage] = useState<string | null>(null);
  const [playing, setPlaying] = useState(false);
  const [speed, setSpeed] = useState<LiveSpeed>(60);

  // Live board: a change anywhere (FHIR Subscription, a handover, our own Try session) makes everything refetch, once
  // per burst (one new observation set is seven FHIR notifications).
  const refetchTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const liveStatus = useLiveChanges(() => {
    clearTimeout(refetchTimer.current);
    refetchTimer.current = setTimeout(() => setRevision((n) => n + 1), 300);
  }, trySession?.id);
  useEffect(() => () => clearTimeout(refetchTimer.current), []);

  const health = useLatest((signal) => fetchHealth(signal), []);
  const ward = useLatest((signal) => fetchWard(view.at, lang, signal, trySession?.id), [view.at, lang, trySession?.id, revision]);
  const at = view.at ?? ward.data?.at;
  const onHandover = view.view === 'handover';
  const patient = useLatest(
    (signal) => (!onHandover && view.bed && at ? fetchPatient(view.bed, at, lang, signal, trySession?.id) : Promise.resolve(undefined)),
    [onHandover, view.bed, at, lang, trySession?.id, revision],
    view.bed,
  );
  const handover = useLatest(
    (signal) => (onHandover && at ? fetchHandover(at, lang, signal, trySession?.id) : Promise.resolve(undefined)),
    [onHandover, at, lang, trySession?.id, revision],
  );

  useEffect(() => {
    const search = writeUrlState(view);
    if (search !== window.location.search) {
      window.history.replaceState(null, '', `${window.location.pathname}${search}`);
    }
  }, [view]);

  // A Try session the API no longer knows (expired, restarted) ends Try mode with a message; other 404s do not.
  const expired = !!trySession && [ward.error, patient.error, handover.error].some(isSessionGone);
  useEffect(() => {
    if (expired) {
      setTrySession(null);
      setTryMessage(t.tryExpired);
    }
  }, [expired, t.tryExpired]);

  const selectBed = useCallback((bed: string) => {
    setView((v) => ({ ...v, bed }));
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    document.querySelector('.detail')?.scrollIntoView?.({ block: 'start', behavior: reduce ? 'auto' : 'smooth' });
  }, []);
  const setAt = useCallback((at: string) => setView((v) => ({ ...v, at })), []);
  const moveClock = useCallback(
    (at: string) => {
      setPlaying(false);
      setAt(at);
    },
    [setAt],
  );

  // The live clock: every 2 s the ward moves speed × 2 s of scenario time, and stops at the shift end. A tick waits
  // while the ward for the current moment is still loading, so a slow server is never asked to start over.
  const shiftEnd = ward.data?.shiftEnd;
  const clockNow = useRef<string | undefined>(undefined);
  clockNow.current = at;
  const wardLoading = useRef(false);
  wardLoading.current = ward.loading;
  useEffect(() => {
    if (!playing || !shiftEnd) {
      return undefined;
    }
    const timer = setInterval(() => {
      if (wardLoading.current || !clockNow.current) {
        return;
      }
      const next = nextLiveAt(clockNow.current, shiftEnd, speed, 2000);
      clockNow.current = next;
      wardLoading.current = true;
      setView((v) => ({ ...v, at: next }));
      if (next === shiftEnd) {
        setPlaying(false);
      }
    }, 2000);
    return () => clearInterval(timer);
  }, [playing, speed, shiftEnd]);
  const showView = (next: 'board' | 'handover') => setView((v) => ({ ...v, view: next === 'handover' ? 'handover' : undefined }));

  const takeOver = async (patientId: string): Promise<string | null> => {
    if (!at) {
      return null;
    }
    try {
      await confirmHandover(patientId, at, lang);
      setRevision((n) => n + 1);
      return null;
    } catch (e) {
      return e instanceof ApiError ? e.title : String(e);
    }
  };
  const data = ward.data;

  const tryAvailable = health.data?.tryAvailable === true;
  const tryReason = health.data && !tryAvailable ? t.tryNeedsMemory : tryAvailable && !view.bed ? t.tryChooseBed : null;

  const begin = async () => {
    if (!view.bed || !at) {
      return;
    }
    setTryMessage(null);
    try {
      const started = await startTry(view.bed, at);
      setTrySession({ id: started.sessionId, patientId: started.patientId, lastActionAt: started.startedAt });
    } catch {
      setTryMessage(t.tryStartFailed);
    }
  };

  const reset = () => {
    if (trySession) {
      void endTry(trySession.id).catch(() => undefined);
    }
    setTrySession(null);
  };

  const act = async (kind: TryActionKind, result: DialogResult): Promise<string | null> => {
    if (!trySession || !at) {
      return null;
    }
    try {
      const saved = await addTryAction(trySession.id, { kind, at, text: result.text || undefined, tier: result.tier });
      setTrySession((s) => (s ? { ...s, lastActionAt: saved.lastActionAt } : s));
      setRevision((n) => n + 1);
      return null;
    } catch (e) {
      if (isSessionGone(e)) {
        setTrySession(null);
        setTryMessage(t.tryExpired);
        return null;
      }
      return e instanceof ApiError ? (e.detail ?? e.title) : String(e);
    }
  };

  const tryBed = trySession ? data?.beds.find((b) => b.patientId === trySession.patientId) : undefined;
  const bedLabel = (bed: string | null | undefined, id: string) => (bed ? t.bed(bed) : id);

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
        <div className="seg" role="group" aria-label={t.viewLabel}>
          <button type="button" aria-pressed={!onHandover} onClick={() => showView('board')}>
            {t.viewBoard}
          </button>
          <button type="button" aria-pressed={onHandover} onClick={() => showView('handover')}>
            {t.viewHandover}
          </button>
        </div>
        <div className="mode">
          <div className="seg" role="group" aria-label={t.modeLabel}>
            <button type="button" aria-pressed={!trySession} onClick={reset}>
              {t.modeWatch}
            </button>
            {/* Pressed while Try is on: clicking it again never restarts Try (that would drop the actions or switch patient). */}
            <button
              type="button"
              aria-pressed={!!trySession}
              disabled={!trySession && (!tryAvailable || !view.bed)}
              onClick={() => (trySession ? undefined : void begin())}
            >
              {t.modeTry}
            </button>
          </div>
          {!trySession && tryReason && <span className="reason">{tryReason}</span>}
        </div>
        <LanguageToggle />
        <p className="live-link muted" role="status">
          {t.liveLink(liveStatus)}
        </p>
      </header>

      {tryMessage && (
        <p className="try-message" role="status">
          {tryMessage}
        </p>
      )}

      {trySession && (
        <section className="try-banner" aria-label={t.modeTry}>
          <p role="status">
            <strong>{t.tryBanner(bedLabel(tryBed?.bed, trySession.patientId), tryBed?.name ?? '')}</strong> <span>{t.tryNote}</span>
          </p>
          <button type="button" className="btn" onClick={reset}>
            {t.tryReset}
          </button>
        </section>
      )}

      {data && (
        <section className="controls" aria-label={t.timeLabel}>
          <TimeControl shiftStart={data.shiftStart} shiftEnd={data.shiftEnd} at={view.at ?? data.at} onChange={moveClock} onUserMove={() => setPlaying(false)} quiet={playing} />
          <LiveControl
            playing={playing}
            speed={speed}
            atEnd={Date.parse(view.at ?? data.at) >= Date.parse(data.shiftEnd)}
            onPlay={() => setPlaying(true)}
            onPause={() => setPlaying(false)}
            onSpeed={setSpeed}
          />
          <p className="hint muted">{t.timeHint}</p>
          <p className="source muted">{t.dataSource(data.source)}</p>
        </section>
      )}

      {ward.error && !expired && (
        <div className="alert" role="alert">
          <p>{ward.error instanceof ApiError && ward.error.status === 503 ? t.fhirDown : t.loadError}</p>
          <button type="button" className="btn" onClick={ward.retry}>
            {t.retry}
          </button>
        </div>
      )}

      {onHandover && !ward.error && (
        <>
          {handover.error && !expired ? (
            <div className="alert" role="alert">
              <p>{handover.error instanceof ApiError && handover.error.status === 503 ? t.fhirDown : t.handoverError}</p>
              <button type="button" className="btn" onClick={handover.retry}>
                {t.retry}
              </button>
            </div>
          ) : handover.data ? (
            <HandoverList response={handover.data} selectedId={view.bed} onSelect={selectBed} />
          ) : (
            <p className="muted">{t.handoverLoading}</p>
          )}
          <section className="detail" aria-label={t.handoverTitle}>
            {(() => {
              const row = handover.data?.rows.find((r) => r.patientId === view.bed);
              return row && handover.data ? (
                <SbarPanel key={`${row.patientId}-${handover.data.at}`} row={row} canConfirm={handover.data.canConfirm} onConfirm={() => takeOver(row.patientId)} />
              ) : (
                <p className="muted">{t.handoverSelectHint}</p>
              );
            })()}
          </section>
        </>
      )}

      {/* After an error the last answer belongs to another moment than the clock shows: never display it. */}
      {onHandover ? null : ward.error ? null : data ? <WardBoard ward={data} selectedId={view.bed} onSelect={selectBed} /> : <BoardSkeleton />}

      {!onHandover && !ward.error && (
        <section className="detail" aria-label={t.observations}>
          {patient.error && !expired ? (
            <div className="alert" role="alert">
              <p>{t.patientError}</p>
              <button type="button" className="btn" onClick={patient.retry}>
                {t.retry}
              </button>
            </div>
          ) : data && patient.data && at ? (
            <PatientDetail
              series={patient.data}
              at={at}
              shiftStart={data.shiftStart}
              shiftEnd={data.shiftEnd}
              actions={
                trySession && patient.data.patient.id === trySession.patientId ? (
                  <ActionPanel
                    patientName={patient.data.patient.name}
                    bedLabel={bedLabel(patient.data.patient.bed, patient.data.patient.id)}
                    at={at}
                    lastActionAt={trySession.lastActionAt}
                    state={patient.data.escalation}
                    onAction={act}
                  />
                ) : undefined
              }
            />
          ) : (
            <p className="muted">{view.bed && patient.loading ? t.patientLoading : t.selectHint}</p>
          )}
        </section>
      )}

      <footer className="foot muted">{t.syntheticDetail}</footer>
    </div>
  );
}
