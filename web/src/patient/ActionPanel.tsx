import { useState } from 'react';
import type { TryActionKind } from '../api/client';
import type { EscalationState, EscalationTier } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { clock, minutesBetween } from '../time';
import { ConfirmDialog, type DialogResult } from '../ui/ConfirmDialog';

interface ActionPanelProps {
  patientName: string;
  bedLabel: string;
  at: string;
  lastActionAt: string;
  state: EscalationState;
  /** Records the action at the current clock time; resolves to the reason when the API refuses it. */
  onAction: (kind: TryActionKind, result: DialogResult) => Promise<string | null>;
}

interface Pending {
  kind: TryActionKind;
  label: string;
  withNote: boolean;
  withTier: boolean;
  tier?: EscalationTier;
}

/**
 * Try mode's actions on the patient screen. Each opens a dialog naming the patient and bed. The rules shown here mirror
 * the API's (it decides): actions only at or after the last one, an assessment once the doctor is there, a plan after
 * the assessment.
 */
export function ActionPanel({ patientName, bedLabel, at, lastActionAt, state, onAction }: ActionPanelProps) {
  const { t } = useT();
  const [pending, setPending] = useState<Pending | null>(null);
  const [refused, setRefused] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // The latest call made by now decides: an assessment or plan belongs to the clinician just called, never an earlier team.
  const latest = [...state.calls].filter((c) => minutesBetween(c.calledAt, at) >= 0).sort((a, b) => Date.parse(a.calledAt) - Date.parse(b.calledAt)).at(-1);
  const clockOk = minutesBetween(lastActionAt, at) >= 0 && !busy;
  const arrived = !!latest?.arrivedAt && minutesBetween(latest.arrivedAt, at) >= 0;
  const assessed = !!latest?.assessedAt && minutesBetween(latest.assessedAt, at) >= 0;
  const suggested = state.suggestedNextTier;

  const open = (p: Pending) => {
    setRefused(null);
    setPending(p);
  };

  const confirm = async (result: DialogResult) => {
    const kind = pending!.kind;
    setPending(null);
    setBusy(true);
    try {
      setRefused(await onAction(kind, result));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="actions" aria-label={t.tryActions}>
      <h3>{t.tryActions}</h3>
      {!clockOk && !busy && <p className="reason">{t.reasonClock(clock(lastActionAt))}</p>}
      <div className="action-buttons">
        <button type="button" className="btn" disabled={!clockOk} onClick={() => open({ kind: 'Concern', label: t.actConcern, withNote: true, withTier: false })}>
          {t.actConcern}
        </button>
        <button type="button" className="btn" disabled={!clockOk} onClick={() => open({ kind: 'PatientOrFamilyConcern', label: t.actFamily, withNote: true, withTier: false })}>
          {t.actFamily}
        </button>
        <button type="button" className="btn" disabled={!clockOk} onClick={() => open({ kind: 'Call', label: t.actCall, withNote: false, withTier: true })}>
          {t.actCall}
        </button>
        {suggested && (
          <button
            type="button"
            className="btn warn"
            disabled={!clockOk}
            onClick={() =>
              open({ kind: 'Call', label: suggested === 'CriticalCare' ? t.actCritical : t.actSenior, withNote: false, withTier: true, tier: suggested })
            }
          >
            {suggested === 'CriticalCare' ? t.actCritical : t.actSenior}
          </button>
        )}
        <button type="button" className="btn" disabled={!clockOk || !arrived} onClick={() => open({ kind: 'Assessed', label: t.actAssess, withNote: true, withTier: false })}>
          {t.actAssess}
        </button>
        <button type="button" className="btn" disabled={!clockOk || !assessed} onClick={() => open({ kind: 'Planned', label: t.actPlan, withNote: true, withTier: false })}>
          {t.actPlan}
        </button>
      </div>
      {clockOk && !arrived && <p className="reason">{t.reasonNoArrival}</p>}
      {clockOk && arrived && !assessed && <p className="reason">{t.reasonNoAssessment}</p>}
      {refused && (
        <p className="reason refused" role="alert">
          {refused}
        </p>
      )}
      {pending && (
        <ConfirmDialog
          title={t.dialogTitle(pending.label, bedLabel, patientName)}
          withNote={pending.withNote}
          withTier={pending.withTier}
          defaultTier={pending.tier}
          onConfirm={(r) => void confirm(r)}
          onCancel={() => setPending(null)}
        />
      )}
    </section>
  );
}
