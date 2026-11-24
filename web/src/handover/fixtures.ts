import type { HandoverResponse, HandoverRow, Sbar } from '../api/types';

// Handover test data shaped like Baton.Api's /api/handover JSON. All patients are invented.

const obs = (time: string) => [
  { reference: `Observation/obs-synthetic-02-${time}-rr`, at: '2026-11-24T07:00:00+03:00', label: 'Observations 07:00' },
  { reference: `Observation/obs-synthetic-02-${time}-hr`, at: '2026-11-24T07:00:00+03:00', label: 'Observations 07:00' },
];

export function sbar(overrides: Partial<Sbar> = {}): Sbar {
  return {
    at: '2026-11-24T08:00:00+03:00',
    windowStart: '2026-11-23T20:00:00+03:00',
    language: 'en',
    sections: [
      {
        part: 'Situation',
        sentences: [
          { text: 'NEWS2 3 Low at 07:00.', sources: obs('202611240700') },
          { text: 'Highest this shift: 10 High at 00:15.', sources: obs('202611240015') },
        ],
      },
      {
        part: 'Background',
        sentences: [{ text: '71, Urinary tract infection.', sources: [{ reference: 'Patient/synthetic-02', at: '2026-11-24T08:00:00+03:00', label: 'Patient record' }] }],
      },
      {
        part: 'Assessment',
        sentences: [{ text: 'On-call doctor called at 23:19.', sources: [{ reference: 'Task/call-synthetic-02-1', at: '2026-11-23T23:19:00+03:00', label: 'On-call doctor call 23:19' }] }],
      },
      {
        part: 'Recommendation',
        sentences: [{ text: 'Next observation due 11:00.', sources: obs('202611240700') }],
      },
    ],
    missing: [{ kind: 'NoPlan', text: 'No plan recorded for the on-call doctor call', sources: [{ reference: 'Task/call-synthetic-02-1', at: '2026-11-23T23:19:00+03:00', label: 'On-call doctor call 23:19' }] }],
    stateLine: 'On-call doctor assessed 23:45',
    currentTotal: 3,
    currentRisk: 'Low',
    peakTotal: 10,
    floorRisk: 'Low',
    ...overrides,
  };
}

export function handoverRow(overrides: Partial<HandoverRow> = {}): HandoverRow {
  return {
    patientId: 'synthetic-02',
    bed: '02',
    name: 'Ayşe K.',
    sbar: sbar(),
    confirmed: null,
    newDataSinceHandover: false,
    ...overrides,
  };
}

export function handover(rows: HandoverRow[], overrides: Partial<HandoverResponse> = {}): HandoverResponse {
  return {
    at: '2026-11-24T08:00:00+03:00',
    windowStart: '2026-11-23T20:00:00+03:00',
    canConfirm: true,
    confirmedCount: rows.filter((r) => r.confirmed).length,
    rows,
    ...overrides,
  };
}
