import { useState } from 'react';
import { ApiError } from '../api/client';
import { shiftAction } from '../api/shiftClient';
import type { DoctorCall, ShiftActionKind } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { riskName, tierOf } from '../risk';
import { clock } from '../time';
import { shiftRefusalText } from '../try/refusal';
import { ConfirmDialog, type DialogResult } from '../ui/ConfirmDialog';

interface DoctorViewProps {
  shiftId: string;
  calls: DoctorCall[] | undefined;
  onChanged: () => void;
}

interface Pending {
  call: DoctorCall;
  kind: 'Assess' | 'Plan';
  label: string;
}

/**
 * The called clinician's screen (decision 23): every call without a plan, the most pressing first, with the patient's
 * latest score and the next step — acknowledge, arrive, assessment, plan. Each step is stamped with the ward clock.
 */
export function DoctorView({ shiftId, calls, onChanged }: DoctorViewProps) {
  const { t } = useT();
  const [pending, setPending] = useState<Pending | null>(null);
  const [messages, setMessages] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState<string | null>(null);
  const tierName = (tier: string) => t[`tier${tier}` as 'tierOnCallDoctor'];

  const act = async (call: DoctorCall, kind: ShiftActionKind, text?: string) => {
    setBusy(call.patientId);
    setMessages((m) => ({ ...m, [call.patientId]: '' }));
    try {
      await shiftAction(shiftId, call.patientId, kind, text);
      onChanged();
    } catch (e) {
      const text = e instanceof ApiError ? shiftRefusalText(e.type, e.detail ?? e.title, t) : String(e);
      setMessages((m) => ({ ...m, [call.patientId]: text }));
    } finally {
      setBusy(null);
    }
  };

  const confirm = (result: DialogResult) => {
    const p = pending!;
    setPending(null);
    void act(p.call, p.kind, result.text);
  };

  return (
    <section className="doctor" aria-label={t.doctorTitle}>
      <h2>{t.doctorTitle}</h2>
      {calls && calls.length === 0 && <p className="muted">{t.doctorEmpty}</p>}
      <ol className="doctor-calls">
        {(calls ?? []).map((c) => {
          const tier = tierOf(c.latest);
          const bed = c.bed ? t.bed(c.bed) : c.patientId;
          const step = c.step;
          return (
            <li key={c.call.callId} className="doctor-call" data-tier={tier ?? undefined} data-passed={c.targetPassed || undefined}>
              <div className="doctor-head">
                <h3>
                  {bed} · {c.name}
                </h3>
                {c.latest?.total !== null && c.latest?.total !== undefined ? (
                  <span className="doctor-score">
                    <span className="score mono">{c.latest.total}</span> <span className="risk">{riskName(c.latest.risk, t)}</span>
                  </span>
                ) : (
                  <span className="muted">{t.docNoScore}</span>
                )}
              </div>
              <p className="doctor-meta">
                <span>{t.docCalled(tierName(c.call.tier), clock(c.call.calledAt), c.minutesSinceCall)}</span>
                {c.call.targetAt && !c.call.arrivedAt && (
                  <span className="doctor-target mono" data-passed={c.targetPassed || undefined}>
                    {c.targetPassed ? t.targetPassed : t.docTarget(clock(c.call.targetAt))}
                  </span>
                )}
                {c.call.acknowledgedAt && <span>{t.docAcknowledgedAt(clock(c.call.acknowledgedAt))}</span>}
                {c.call.arrivedAt && <span>{t.docArrivedAt(clock(c.call.arrivedAt))}</span>}
                {c.latest && <span className="muted">{t.docLatest(clock(c.latest.at))}</span>}
              </p>
              <div className="action-buttons">
                {step === 'Called' && (
                  <button type="button" className="btn primary" disabled={busy === c.patientId} onClick={() => void act(c, 'Acknowledge')}>
                    {t.docAcknowledge}
                  </button>
                )}
                {(step === 'Called' || step === 'Acknowledged') && (
                  <button type="button" className={step === 'Acknowledged' ? 'btn primary' : 'btn'} disabled={busy === c.patientId} onClick={() => void act(c, 'Arrive')}>
                    {t.docArrive}
                  </button>
                )}
                {step === 'Arrived' && (
                  <button type="button" className="btn primary" disabled={busy === c.patientId} onClick={() => setPending({ call: c, kind: 'Assess', label: t.docAssess })}>
                    {t.docAssess}
                  </button>
                )}
                {step === 'Assessed' && (
                  <button type="button" className="btn primary" disabled={busy === c.patientId} onClick={() => setPending({ call: c, kind: 'Plan', label: t.docPlan })}>
                    {t.docPlan}
                  </button>
                )}
              </div>
              {messages[c.patientId] && (
                <p className="reason refused" role="alert">
                  {messages[c.patientId]}
                </p>
              )}
            </li>
          );
        })}
      </ol>
      {pending && (
        <ConfirmDialog
          title={t.dialogTitle(pending.label, pending.call.bed ? t.bed(pending.call.bed) : pending.call.patientId, pending.call.name)}
          withNote
          onConfirm={confirm}
          onCancel={() => setPending(null)}
        />
      )}
    </section>
  );
}
