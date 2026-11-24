import { useEffect, useId, useRef, useState } from 'react';
import { ApiError } from '../api/client';
import { measure, previewScore, saveObservation, shiftAction } from '../api/shiftClient';
import type { Consciousness, EscalationTier, News2Preview, ShiftObservationResult, SubScoreKey, VitalSet } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import type { Messages } from '../i18n/en';
import { riskName, tierOfRisk } from '../risk';
import { clock } from '../time';
import { shiftRefusalText } from '../try/refusal';

type FieldKey = Exclude<keyof VitalSet, 'consciousness'>;

const numberFields: { key: FieldKey; score: SubScoreKey }[] = [
  { key: 'respirationRate', score: 'respirationRate' },
  { key: 'spO2', score: 'spO2' },
  { key: 'oxygenLitresPerMinute', score: 'supplementalOxygen' },
  { key: 'systolicBp', score: 'systolicBp' },
  { key: 'pulseRate', score: 'pulseRate' },
  { key: 'temperatureCelsius', score: 'temperature' },
];

const levels: Consciousness[] = ['Alert', 'Confusion', 'Voice', 'Pain', 'Unresponsive'];

export function fieldLabel(key: string, t: Messages): string {
  switch (key) {
    case 'respirationRate':
      return t.fieldRespirationRate;
    case 'spO2':
      return t.fieldSpO2;
    case 'oxygenLitresPerMinute':
      return t.fieldOxygen;
    case 'systolicBp':
      return t.fieldSystolicBp;
    case 'pulseRate':
      return t.fieldPulseRate;
    case 'temperatureCelsius':
      return t.fieldTemperature;
    default:
      return t.fieldConsciousness;
  }
}

type Draft = Record<FieldKey, string> & { consciousness: Consciousness };

const empty: Draft = {
  respirationRate: '',
  spO2: '',
  oxygenLitresPerMinute: '0',
  systolicBp: '',
  pulseRate: '',
  temperatureCelsius: '',
  consciousness: 'Alert',
};

/** The draft as a set, or null while a field is empty or not a number. */
function toVitals(draft: Draft): VitalSet | null {
  const n = (s: string) => (s.trim() === '' ? NaN : Number(s.replace(',', '.')));
  const values = numberFields.map((f) => n(draft[f.key]));
  if (values.some((v) => Number.isNaN(v))) {
    return null;
  }
  const [respirationRate, spO2, oxygenLitresPerMinute, systolicBp, pulseRate, temperatureCelsius] = values as [number, number, number, number, number, number];
  return { respirationRate, spO2, oxygenLitresPerMinute, systolicBp, pulseRate, temperatureCelsius, consciousness: draft.consciousness };
}

/** The tier the score suggests calling: critical care at 7+ (RCP emergency response), otherwise the on-call doctor. */
export function suggestedTier(risk: string | null): EscalationTier | null {
  if (risk === 'High') return 'CriticalCare';
  return risk === 'Medium' || risk === 'LowMedium' ? 'OnCallDoctor' : null;
}

interface ObservationFormProps {
  shiftId: string;
  patientId: string;
  bedLabel: string;
  name: string;
  onSaved: () => void;
  onClose: () => void;
}

/**
 * The bedside entry form (phone width): "Measure now" fills in what the monitor reads, the nurse checks and corrects,
 * the score from the API's own engine updates as they type (decision 25), and after saving it says what to do next.
 */
export function ObservationForm({ shiftId, patientId, bedLabel, name, onSaved, onClose }: ObservationFormProps) {
  const { t } = useT();
  const titleId = useId();
  const dialog = useRef<HTMLDialogElement>(null);
  const [draft, setDraft] = useState<Draft>(empty);
  const [preview, setPreview] = useState<News2Preview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState<ShiftObservationResult | null>(null);
  const [called, setCalled] = useState(false);
  const close = useRef(onClose);
  close.current = onClose;

  useEffect(() => {
    const element = dialog.current;
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    if (element && typeof element.showModal === 'function' && !element.open) {
      try {
        element.showModal();
      } catch {
        element.setAttribute('open', '');
      }
    } else {
      element?.setAttribute('open', '');
    }
    const onCancel = (e: Event) => {
      e.preventDefault();
      close.current();
    };
    element?.addEventListener('cancel', onCancel);
    return () => {
      element?.removeEventListener('cancel', onCancel);
      if (element?.open && typeof element.close === 'function') {
        element.close();
      }
      opener?.focus();
    };
  }, []);

  // Live score: the engine scores the draft 250 ms after the last keystroke; nothing is stored.
  const vitals = toVitals(draft);
  const key = vitals ? JSON.stringify(vitals) : '';
  useEffect(() => {
    if (!key) {
      setPreview(null);
      return undefined;
    }
    const controller = new AbortController();
    const timer = setTimeout(() => {
      previewScore(JSON.parse(key) as VitalSet, patientId, shiftId, controller.signal)
        .then(setPreview)
        .catch(() => setPreview(null));
    }, 250);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [key, patientId, shiftId]);

  const explain = (e: unknown) =>
    e instanceof ApiError ? shiftRefusalText(e.type, e.detail ?? e.title, t, e.field ? fieldLabel(e.field, t) : undefined) : String(e);

  const fill = async () => {
    setError(null);
    try {
      const m = await measure(shiftId, patientId);
      setDraft({
        respirationRate: String(m.vitals.respirationRate),
        spO2: String(m.vitals.spO2),
        oxygenLitresPerMinute: String(m.vitals.oxygenLitresPerMinute),
        systolicBp: String(m.vitals.systolicBp),
        pulseRate: String(m.vitals.pulseRate),
        temperatureCelsius: String(m.vitals.temperatureCelsius),
        consciousness: m.vitals.consciousness,
      });
    } catch (e) {
      setError(explain(e));
    }
  };

  const save = async () => {
    if (!vitals) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      setSaved(await saveObservation(shiftId, patientId, vitals));
      onSaved();
    } catch (e) {
      setError(explain(e));
    } finally {
      setBusy(false);
    }
  };

  const call = async (tier: EscalationTier) => {
    setBusy(true);
    setError(null);
    try {
      await shiftAction(shiftId, patientId, 'Call', undefined, tier);
      setCalled(true);
      onSaved();
    } catch (e) {
      setError(explain(e));
    } finally {
      setBusy(false);
    }
  };

  const shown = saved?.latest ?? null;
  const risk = shown ? shown.risk : (preview?.risk ?? null);
  const total = shown ? shown.total : (preview?.total ?? null);
  const tier = tierOfRisk(risk, total);
  const response = shown?.response ?? preview?.response ?? null;
  const callTier = saved ? suggestedTier(saved.latest?.risk ?? null) : null;
  const tierName = (x: EscalationTier) => t[`tier${x}`];

  return (
    <div className="dialog-backdrop">
      <dialog ref={dialog} aria-modal="true" aria-labelledby={titleId} className="confirm obs-form">
        <form
          method="dialog"
          onSubmit={(e) => {
            e.preventDefault();
            void save();
          }}
        >
          <h2 id={titleId}>{t.obsTitle(bedLabel, name)}</h2>
          {!saved && (
            <>
              <div className="obs-measure">
                <button type="button" className="btn" onClick={() => void fill()}>
                  {t.obsMeasure}
                </button>
                <span className="muted">{t.obsMeasureHint}</span>
              </div>
              <div className="obs-fields">
                {numberFields.map((f) => {
                  const sub = preview?.subScores[f.score];
                  return (
                    <label key={f.key} className="obs-field">
                      <span>{fieldLabel(f.key, t)}</span>
                      <span className="obs-input">
                        <input
                          inputMode="decimal"
                          type="text"
                          autoComplete="off"
                          value={draft[f.key]}
                          onChange={(e) => setDraft((d) => ({ ...d, [f.key]: e.target.value }))}
                          required
                        />
                        {sub !== undefined && sub !== null && (
                          <span className="obs-sub mono" data-score={sub} aria-hidden="true">
                            +{sub}
                          </span>
                        )}
                      </span>
                    </label>
                  );
                })}
                <label className="obs-field">
                  <span>{t.fieldConsciousness}</span>
                  <span className="obs-input">
                    <select value={draft.consciousness} onChange={(e) => setDraft((d) => ({ ...d, consciousness: e.target.value as Consciousness }))}>
                      {levels.map((l) => (
                        <option key={l} value={l}>
                          {t[`acvpu${l}`]}
                        </option>
                      ))}
                    </select>
                    {preview?.subScores.consciousness !== undefined && preview.subScores.consciousness !== null && (
                      <span className="obs-sub mono" data-score={preview.subScores.consciousness} aria-hidden="true">
                        +{preview.subScores.consciousness}
                      </span>
                    )}
                  </span>
                </label>
              </div>
            </>
          )}

          <div className="obs-result" data-tier={tier ?? undefined} aria-live="polite">
            <p className="obs-label muted">{saved ? t.obsSaved(clock(saved.latest?.at ?? '')) : t.obsPreview}</p>
            {total !== null ? (
              <p className="obs-score">
                <span className="score mono">{total}</span> <span className="risk">{riskName(risk, t)}</span>
              </p>
            ) : (
              <p className="muted">{t.obsPreviewIncomplete}</p>
            )}
            {response && <p className="obs-response">{t[`response${response}`]}</p>}
            {saved?.nextDue && <p className="obs-next">{t.nextDueAt(clock(saved.nextDue))}</p>}
          </div>

          {error && (
            <p className="reason refused" role="alert">
              {error}
            </p>
          )}

          <div className="dialog-actions">
            <button type="button" className="btn" onClick={onClose}>
              {saved ? t.obsClose : t.cancel}
            </button>
            {!saved && (
              <button type="submit" className="btn confirm" disabled={!vitals || busy}>
                {busy ? t.obsSaving : t.obsSave}
              </button>
            )}
            {saved && callTier && !called && (
              <button type="button" className="btn escalate" disabled={busy} onClick={() => void call(callTier)}>
                {t.actCallNow(tierName(callTier))}
              </button>
            )}
          </div>
          {called && callTier && (
            <p role="status" className="obs-called">
              {t.recCalled(tierName(callTier))}
            </p>
          )}
        </form>
      </dialog>
    </div>
  );
}
