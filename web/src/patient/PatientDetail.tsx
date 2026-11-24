import type { PatientSeries } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { riskText, shapeOf, tierOf } from '../risk';
import { clock } from '../time';
import { Shape } from '../ui/Icon';
import { ChainPanel } from './ChainPanel';
import { EventTimeline } from './EventTimeline';
import { ObservationTable } from './ObservationTable';
import { ScoreChart } from './ScoreChart';
import './patient.css';

interface PatientDetailProps {
  series: PatientSeries;
  at: string;
  shiftStart: string;
  shiftEnd: string;
}

/** The selected patient: NEWS2 over the shift with events, the event timeline and every observation set. */
export function PatientDetail({ series, at, shiftStart, shiftEnd }: PatientDetailProps) {
  const { t } = useT();
  const { patient, points } = series;
  const latest = points.at(-1) ?? null;
  const tier = tierOf(latest);
  const label = patient.bed ? t.bed(patient.bed) : patient.id;

  return (
    <div className="patient">
      <div className="patient-head">
        <div>
          <h2>
            {label} · {patient.name}
            {series.age !== null ? `, ${t.age(series.age)}` : ''}
          </h2>
          {series.diagnosis && <p className="muted">{series.diagnosis}</p>}
        </div>
        {latest && (
          <p className="pill" data-tier={tier ?? 'none'}>
            <Shape shape={shapeOf(tier)} />
            <span className="mono">NEWS2 {latest.total ?? '—'}</span> · {riskText(latest, t)} · <span className="mono">{clock(latest.at)}</span>
          </p>
        )}
      </div>

      <div className="patient-grid">
        <ScoreChart
          label={t.chartLabel(label, clock(shiftStart), clock(shiftEnd))}
          points={points}
          events={series.events}
          at={at}
          shiftStart={shiftStart}
          shiftEnd={shiftEnd}
        />
        <div className="side">
          <ChainPanel state={series.escalation} records={series.escalationRecords} />
          <EventTimeline events={series.events} available={series.eventsAvailable} shiftStart={shiftStart} />
        </div>
      </div>

      <h3>{t.observations}</h3>
      <ObservationTable points={points} scale2={patient.spO2Scale === 'Scale2'} />
    </div>
  );
}
