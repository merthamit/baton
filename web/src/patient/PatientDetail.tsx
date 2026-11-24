import type { ReactNode } from 'react';
import type { EscalationRecordView, PatientSeries, WardEvent } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { riskText, shapeOf, tierOf } from '../risk';
import { clock } from '../time';
import { Shape } from '../ui/Icon';
import { ChainPanel } from './ChainPanel';
import { EventTimeline } from './EventTimeline';
import { ObservationTable } from './ObservationTable';
import { ScoreChart, type ChartMarker } from './ScoreChart';
import './patient.css';

/** Chart markers from the escalation records (the assessment shares the arrival's marker) and Baton's alerts. */
function markersOf(records: EscalationRecordView[], events: WardEvent[]): ChartMarker[] {
  const fromRecords = records.flatMap<ChartMarker>((r, i) => {
    const key = `${r.kind}-${r.at}-${r.by}-${i}`;
    switch (r.kind) {
      case 'ConcernRaised':
      case 'PatientOrFamilyConcern':
        return [{ at: r.at, icon: 'flag', key }];
      case 'Called':
        return [{ at: r.at, icon: r.tier === 'CriticalCare' ? 'critical' : 'phone', key }];
      case 'Arrived':
        return [{ at: r.at, icon: 'doctor', key }];
      case 'Planned':
        return [{ at: r.at, icon: 'drop', key }];
      default:
        return [];
    }
  });
  const alerts = events.filter((e) => e.kind === 'Alert').map<ChartMarker>((e) => ({ at: e.at, icon: 'bell', key: `alert-${e.at}` }));
  return [...fromRecords, ...alerts];
}

interface PatientDetailProps {
  series: PatientSeries;
  at: string;
  shiftStart: string;
  shiftEnd: string;
  /** Try mode's action panel for this patient, when Try is on for them. */
  actions?: ReactNode;
}

/** The selected patient: NEWS2 over the shift with events, the event timeline and every observation set. */
export function PatientDetail({ series, at, shiftStart, shiftEnd, actions }: PatientDetailProps) {
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

      {actions}

      <div className="patient-grid">
        <ScoreChart
          label={t.chartLabel(label, clock(shiftStart), clock(shiftEnd))}
          points={points}
          markers={markersOf(series.escalationRecords, series.events)}
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
