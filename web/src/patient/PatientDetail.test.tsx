import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/react';
import { PatientDetail } from './PatientDetail';
import { point } from '../fixtures';
import { renderIn } from '../test/render';
import type { PatientSeries } from '../api/types';

const shiftStart = '2026-11-23T08:00:00+03:00';
const shiftEnd = '2026-11-24T08:00:00+03:00';
const at = '2026-11-24T00:30:00+03:00';

function series(overrides: Partial<PatientSeries> = {}): PatientSeries {
  return {
    patient: {
      id: 'synthetic-02',
      name: 'Ayşe K.',
      bed: '02',
      spO2Scale: 'Scale1',
      birthDate: '1955-08-15',
      diagnosis: 'Community-acquired pneumonia',
    },
    age: 71,
    diagnosis: 'Community-acquired pneumonia',
    points: [
      point({ at: '2026-11-23T08:00:00+03:00', total: 0, risk: 'Low', response: 'RoutineMonitoring' }),
      point({ at: '2026-11-23T20:00:00+03:00', total: 2, risk: 'Low', response: 'WardBasedResponse' }),
      point({ at: '2026-11-23T23:15:00+03:00', total: 6, risk: 'Medium' }),
      point({
        at: '2026-11-24T00:15:00+03:00',
        total: 10,
        risk: 'High',
        response: 'EmergencyResponse',
        subScores: {
          respirationRate: 3,
          spO2: 2,
          supplementalOxygen: 2,
          systolicBp: 2,
          pulseRate: 1,
          consciousness: 0,
          temperature: 0,
        },
      }),
    ],
    eventsAvailable: true,
    events: [
      { at: '2026-11-23T05:00:00+03:00', kind: 'Note', text: 'Admitted overnight' },
      { at: '2026-11-23T20:15:00+03:00', kind: 'Concern', text: 'Restless, not eating, not herself' },
      { at: '2026-11-23T23:19:00+03:00', kind: 'DoctorCalled', text: 'Nurse called the on-call doctor' },
    ],
    ...overrides,
  };
}

function show(s: PatientSeries = series(), lang: 'en' | 'tr' = 'en') {
  return renderIn(<PatientDetail series={s} at={at} shiftStart={shiftStart} shiftEnd={shiftEnd} />, lang);
}

describe('PatientDetail chart', () => {
  it('is an image labelled with the bed and the shift range', () => {
    show();
    expect(screen.getByRole('img', { name: 'NEWS2 over the shift for Bed 02, 08:00 to 08:00' })).toBeInTheDocument();
  });

  it('draws one point per score, the four NEWS2 bands and a line at the chosen time', () => {
    show();
    const chart = screen.getByRole('img', { name: /NEWS2 over the shift/ });
    expect(chart.querySelectorAll('[data-point]')).toHaveLength(4);
    expect([...chart.querySelectorAll('[data-band]')].map((b) => b.getAttribute('data-band'))).toEqual(['0', '1-4', '5-6', '7+']);
    expect(chart.querySelector('[data-now]')).not.toBeNull();
  });
});

describe('PatientDetail observations', () => {
  it('has one row per score with readings and sub-scores as text', () => {
    show();
    const table = screen.getByRole('table');
    const rows = within(table).getAllByRole('row');
    expect(rows).toHaveLength(1 + 4);

    const last = rows[4]!;
    expect(within(last).getByText('00:15')).toBeInTheDocument();
    const resp = within(last).getByText('22').closest('td')!;
    expect(resp).toHaveAttribute('data-sub', '3');
    expect(resp).toHaveTextContent('22+3');
    expect(within(last).getByText('A').closest('td')).toHaveTextContent(/^A$/);
    expect(within(last).getByText('10')).toBeInTheDocument();
  });

  it('marks Scale 2 in the SpO₂ column header', () => {
    show(series({ patient: { ...series().patient, spO2Scale: 'Scale2' } }));
    expect(screen.getByRole('columnheader', { name: 'SpO₂ (S2)' })).toBeInTheDocument();
  });
});

describe('PatientDetail events', () => {
  it('lists events with clock time, translated kind and the stored text', () => {
    show(series(), 'tr');
    const list = screen.getByRole('list', { name: 'Olaylar' });
    expect(within(list).getByText('Endişe bayrağı')).toBeInTheDocument();
    expect(within(list).getByText('Restless, not eating, not herself')).toBeInTheDocument();
    expect(within(list).getByText('20:15')).toBeInTheDocument();
    expect(within(list).getByText('önce')).toBeInTheDocument();
  });

  it('says when the source has no events', () => {
    show(series({ eventsAvailable: false, events: [] }));
    expect(screen.getByText('Escalation events are not available from this data source yet')).toBeInTheDocument();
  });

  it('says when nothing happened', () => {
    show(series({ events: [] }));
    expect(screen.getByText('No events this shift.')).toBeInTheDocument();
  });
});
