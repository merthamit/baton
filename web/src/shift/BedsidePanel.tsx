import { useId, useState } from 'react';
import { ApiError } from '../api/client';
import { saveNotDone, shiftAction } from '../api/shiftClient';
import type { EscalationState, EscalationTier, NotDoneReason, ShiftActionKind } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { shiftRefusalText } from '../try/refusal';
import { ConfirmDialog, type DialogResult } from '../ui/ConfirmDialog';
import { ObservationForm } from './ObservationForm';

const reasons: NotDoneReason[] = ['PatientAsleep', 'PatientDeclined', 'OffTheWard', 'EquipmentUnavailable', 'EndOfLifeCare', 'CouldNotBeObtained'];

interface BedsidePanelProps {
  shiftId: string;
  patientId: string;
  bedLabel: string;
  name: string;
  state: EscalationState;
  onChanged: () => void;
}

interface Pending {
  kind: ShiftActionKind;
  label: string;
  withNote: boolean;
  withTier: boolean;
  tier?: EscalationTier;
}

/** The nurse at the bedside in a shift: enter a set, record why one was not taken, raise a concern, call. */
export function BedsidePanel({ shiftId, patientId, bedLabel, name, state, onChanged }: BedsidePanelProps) {
  const { t } = useT();
  const reasonId = useId();
  const formId = useId();
  const [entering, setEntering] = useState(false);
  const [notDone, setNotDone] = useState(false);
  const [reason, setReason] = useState<NotDoneReason>('PatientAsleep');
  const [pending, setPending] = useState<Pending | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const suggested = state.suggestedNextTier;

  const run = async (work: () => Promise<unknown>) => {
    setBusy(true);
    setMessage(null);
    try {
      await work();
      onChanged();
    } catch (e) {
      setMessage(e instanceof ApiError ? shiftRefusalText(e.type, e.detail ?? e.title, t) : String(e));
    } finally {
      setBusy(false);
    }
  };

  const confirm = (result: DialogResult) => {
    const p = pending!;
    setPending(null);
    void run(() => shiftAction(shiftId, patientId, p.kind, result.text || undefined, result.tier ?? p.tier));
  };

  return (
    <section className="actions bedside" aria-label={t.bedside}>
      <h3>{t.bedside}</h3>
      <div className="action-buttons">
        <button type="button" className="btn primary" disabled={busy} onClick={() => setEntering(true)}>
          {t.actObserve}
        </button>
        <button type="button" className="btn" disabled={busy} aria-expanded={notDone} aria-controls={formId} onClick={() => setNotDone((v) => !v)}>
          {t.actNotDone}
        </button>
        <button type="button" className="btn concern" disabled={busy} onClick={() => setPending({ kind: 'Concern', label: t.actConcern, withNote: true, withTier: false })}>
          {t.actConcern}
        </button>
        <button type="button" className="btn concern" disabled={busy} onClick={() => setPending({ kind: 'PatientOrFamilyConcern', label: t.actFamily, withNote: true, withTier: false })}>
          {t.actFamily}
        </button>
        <button type="button" className="btn escalate" disabled={busy} onClick={() => setPending({ kind: 'Call', label: t.actCall, withNote: false, withTier: true })}>
          {t.actCall}
        </button>
        {suggested && (
          <button
            type="button"
            className="btn danger"
            disabled={busy}
            onClick={() => setPending({ kind: 'Call', label: suggested === 'CriticalCare' ? t.actCritical : t.actSenior, withNote: false, withTier: true, tier: suggested })}
          >
            {suggested === 'CriticalCare' ? t.actCritical : t.actSenior}
          </button>
        )}
      </div>

      {notDone && (
        <form
          id={formId}
          className="not-done"
          onSubmit={(e) => {
            e.preventDefault();
            setNotDone(false);
            void run(() => saveNotDone(shiftId, patientId, reason));
          }}
        >
          <fieldset>
            <legend id={reasonId}>{t.notDoneReasonLabel}</legend>
            {reasons.map((r) => (
              <label key={r} data-urgent={r === 'CouldNotBeObtained' || undefined}>
                <input type="radio" name="reason" value={r} checked={reason === r} onChange={() => setReason(r)} />
                {t[`reason${r}`]}
              </label>
            ))}
          </fieldset>
          <button type="submit" className="btn primary" disabled={busy}>
            {t.notDoneSave}
          </button>
        </form>
      )}

      {message && (
        <p className="reason refused" role="alert">
          {message}
        </p>
      )}

      {entering && (
        <ObservationForm
          shiftId={shiftId}
          patientId={patientId}
          bedLabel={bedLabel}
          name={name}
          onSaved={onChanged}
          onClose={() => setEntering(false)}
        />
      )}
      {pending && (
        <ConfirmDialog
          title={t.dialogTitle(pending.label, bedLabel, name)}
          withNote={pending.withNote}
          withTier={pending.withTier}
          defaultTier={pending.tier}
          onConfirm={confirm}
          onCancel={() => setPending(null)}
        />
      )}
    </section>
  );
}
