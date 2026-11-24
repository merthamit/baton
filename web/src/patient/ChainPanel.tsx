import { useId } from 'react';
import type { EscalationRecordView, EscalationState } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import type { Messages } from '../i18n/en';
import { clock } from '../time';
import { ChainStrip } from '../board/ChainStrip';

function role(by: string, t: Messages): string {
  if (by.startsWith('RelatedPerson/')) return t.roleFamily;
  if (by.startsWith('Patient/')) return t.rolePatient;
  if (by.endsWith('synthetic-oncall')) return t.roleOnCall;
  if (by.endsWith('synthetic-senior')) return t.roleSenior;
  if (by.endsWith('synthetic-cct')) return t.roleCriticalCare;
  return t.roleNurse;
}

function title(record: EscalationRecordView, t: Messages): string {
  switch (record.kind) {
    case 'ConcernRaised':
      return t.recConcernRaised;
    case 'PatientOrFamilyConcern':
      return t.recPatientOrFamilyConcern;
    case 'Called':
      return t.recCalled(record.tier ? t[`tier${record.tier}`] : '');
    case 'Arrived':
      return t.recArrived;
    case 'Assessed':
      return t.recAssessed;
    case 'Planned':
      return t.recPlanned;
  }
}

/** The patient's escalation chain: the current state and every record with time, who and what. */
export function ChainPanel({ state, records }: { state: EscalationState; records: EscalationRecordView[] }) {
  const { t } = useT();
  const id = useId();
  const ordered = [...records].sort((a, b) => Date.parse(a.at) - Date.parse(b.at));

  return (
    <div className="chain-panel">
      <h3 id={id}>{t.chainTitle}</h3>
      <ChainStrip state={state} />
      {ordered.length === 0 ? (
        <p className="muted">{t.noEscalation}</p>
      ) : (
        <ol aria-labelledby={id}>
          {ordered.map((r) => (
            <li key={`${r.kind}-${r.at}-${r.by}`} data-kind={r.kind}>
              <span className="tm mono">{clock(r.at)}</span>
              <span>
                <b>{title(r, t)}</b> <span className="muted">{role(r.by, t)}</span>
                {r.simulated && <span className="sim">{t.simulated}</span>}
                {r.text && <span className="rec-text">{r.text}</span>}
              </span>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
