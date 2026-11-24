import type { BedSummary, CallState, EscalationState, ScorePoint, WardResponse } from './api/types';

// Test data shaped exactly like Baton.Api's JSON (taken from a real in-memory response). All patients are invented.

export function point(overrides: Partial<ScorePoint> = {}): ScorePoint {
  return {
    at: '2026-11-23T23:15:00+03:00',
    total: 6,
    minimumTotal: 6,
    risk: 'Medium',
    response: 'UrgentResponse',
    minimumMonitoringFrequency: 'At least hourly',
    subScores: {
      respirationRate: 2,
      spO2: 1,
      supplementalOxygen: 0,
      systolicBp: 1,
      pulseRate: 1,
      consciousness: 0,
      temperature: 1,
    },
    parametersScoring3: [],
    problems: [],
    input: {
      respirationRate: 22,
      spO2: 95,
      spO2Scale: 'Scale1',
      onSupplementalOxygen: false,
      systolicBp: 109,
      pulseRate: 103,
      temperatureCelsius: 38.7,
      consciousness: 'Alert',
    },
    notes: [],
    sources: [],
    ...overrides,
  };
}

export function call(overrides: Partial<CallState> = {}): CallState {
  return {
    callId: 'call-synthetic-02-1',
    tier: 'OnCallDoctor',
    calledAt: '2026-11-23T23:19:00+03:00',
    targetAt: '2026-11-24T00:19:00+03:00',
    arrivedAt: null,
    arrivalSimulated: false,
    assessedAt: null,
    assessment: null,
    plannedAt: null,
    plan: null,
    ...overrides,
  };
}

/** Bed 02 at 23:30: concern 20:15, on-call doctor called 23:19, waited 11 of 60 minutes. */
export function chain(overrides: Partial<EscalationState> = {}): EscalationState {
  const open = call();
  return {
    step: 'Called',
    concernRaisedAt: '2026-11-23T20:15:00+03:00',
    concernNote: 'Restless, not eating, not herself',
    patientConcernAt: null,
    calls: [open],
    openCall: open,
    minutesWaited: 11,
    targetPassed: false,
    suggestedNextTier: null,
    ...overrides,
  };
}

export const noChain: EscalationState = {
  step: 'None',
  concernRaisedAt: null,
  concernNote: null,
  patientConcernAt: null,
  calls: [],
  openCall: null,
  minutesWaited: null,
  targetPassed: false,
  suggestedNextTier: null,
};

export function bed(overrides: Partial<BedSummary> = {}): BedSummary {
  return {
    patientId: 'synthetic-02',
    name: 'Ayşe K.',
    bed: '02',
    age: 71,
    diagnosis: 'Community-acquired pneumonia',
    latest: point(),
    previous: point({
      at: '2026-11-23T21:15:00+03:00',
      total: 4,
      minimumTotal: 4,
      risk: 'Low',
      response: 'WardBasedResponse',
    }),
    drivers: ['RespirationRate', 'SpO2', 'SystolicBp', 'PulseRate', 'Temperature'],
    nextDue: '2026-11-24T00:15:00+03:00',
    overdueMinutes: 0,
    concernsKnown: true,
    escalation: chain(),
    eventsAvailable: true,
    events: [],
    ...overrides,
  };
}

export const quietBed = (n: number): BedSummary =>
  bed({
    patientId: `synthetic-${String(n).padStart(2, '0')}`,
    bed: String(n).padStart(2, '0'),
    name: `Patient ${n}`,
    latest: point({
      at: '2026-11-23T20:00:00+03:00',
      total: 0,
      minimumTotal: 0,
      risk: 'Low',
      response: 'RoutineMonitoring',
      subScores: {
        respirationRate: 0,
        spO2: 0,
        supplementalOxygen: 0,
        systolicBp: 0,
        pulseRate: 0,
        consciousness: 0,
        temperature: 0,
      },
    }),
    previous: null,
    drivers: [],
    nextDue: '2026-11-24T08:00:00+03:00',
    escalation: noChain,
  });

export function ward(beds: BedSummary[]): WardResponse {
  return {
    at: '2026-11-23T23:30:00+03:00',
    source: 'InMemory',
    shiftStart: '2026-11-23T08:00:00+03:00',
    shiftEnd: '2026-11-24T08:00:00+03:00',
    beds,
  };
}
