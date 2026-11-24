import { useId, useState, type ReactNode } from 'react';
import type { HandoverRow, SbarPart } from '../api/types';
import type { Messages } from '../i18n/en';
import { useT } from '../i18n/LanguageProvider';
import { clock } from '../time';
import { ConfirmDialog } from '../ui/ConfirmDialog';
import { SourceButton } from './SourceButton';

interface SbarPanelProps {
  row: HandoverRow;
  canConfirm: boolean;
  /** Stores the handover; resolves to the reason when the API refuses it. */
  onConfirm: () => Promise<string | null>;
}

const partLabel = (part: SbarPart, t: Messages) =>
  ({ Situation: t.sbarSituation, Background: t.sbarBackground, Assessment: t.sbarAssessment, Recommendation: t.sbarRecommendation })[part];

/**
 * One patient's SBAR as the API wrote it: four labelled parts, a source button after every sentence, what the record
 * is missing, and "Taken over" behind a confirmation that names the bed and the patient (13A).
 */
export function SbarPanel({ row, canConfirm, onConfirm }: SbarPanelProps) {
  const { t } = useT();
  const headingId = useId();
  const [asking, setAsking] = useState(false);
  const [busy, setBusy] = useState(false);
  const [refused, setRefused] = useState<string | null>(null);
  const bedLabel = row.bed ? t.bed(row.bed) : row.patientId;
  const confirmedNow = !!row.confirmed && Date.parse(row.confirmed.at) === Date.parse(row.sbar.at);

  const confirm = async () => {
    setAsking(false);
    setBusy(true);
    try {
      setRefused(await onConfirm());
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="sbar" aria-labelledby={headingId}>
      <h2 id={headingId}>{t.sbarHeading(bedLabel, row.name)}</h2>
      <p className="muted sbar-note">{t.sbarRuleNote}</p>
      {row.sbar.sections.map((section) => (
        <Part key={section.part} title={partLabel(section.part, t)}>
          {section.sentences.map((sentence, i) => (
            <li key={i}>
              <span>{sentence.text}</span> <SourceButton sources={sentence.sources} />
            </li>
          ))}
        </Part>
      ))}
      {row.sbar.missing.length > 0 && (
        <Part title={t.sbarMissing} tone="bad">
          {row.sbar.missing.map((item) => (
            <li key={item.kind + item.text}>
              <span>{item.text}</span> <SourceButton sources={item.sources} />
            </li>
          ))}
        </Part>
      )}
      <div className="sbar-actions">
        {row.confirmed && (
          <p role="status" className="sbar-confirmed">
            {t.takenOverAt(clock(row.confirmed.at))}
            {row.newDataSinceHandover && <> · {t.newDataSince}</>}
          </p>
        )}
        {!canConfirm ? (
          <p className="reason">{t.handoverTryNote}</p>
        ) : (
          !confirmedNow && (
            <button type="button" className="btn primary" disabled={busy} onClick={() => setAsking(true)}>
              {t.actTakeOver}
            </button>
          )
        )}
        {refused && (
          <p className="reason refused" role="alert">
            {refused}
          </p>
        )}
      </div>
      {asking && <ConfirmDialog title={t.dialogTitle(t.actTakeOver, bedLabel, row.name)} onConfirm={() => void confirm()} onCancel={() => setAsking(false)} />}
    </section>
  );
}

function Part({ title, tone, children }: { title: string; tone?: 'bad'; children: ReactNode }) {
  const id = useId();
  return (
    <section className="sbar-part" data-tone={tone} aria-labelledby={id}>
      <h3 id={id}>{title}</h3>
      <ul>{children}</ul>
    </section>
  );
}
