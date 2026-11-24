// Mirrors of Baton.Api's JSON (System.Text.Json, camelCase properties, enums as strings).
// Note: dictionary keys (subScores) are camelCase ("respirationRate"); enum values are PascalCase ("RespirationRate").

export type RiskLevel = 'Low' | 'LowMedium' | 'Medium' | 'High';

export type ClinicalResponse =
  | 'RoutineMonitoring'
  | 'WardBasedResponse'
  | 'UrgentWardBasedResponse'
  | 'UrgentResponse'
  | 'EmergencyResponse';

export type News2Parameter =
  | 'RespirationRate'
  | 'SpO2'
  | 'SupplementalOxygen'
  | 'SystolicBp'
  | 'PulseRate'
  | 'Consciousness'
  | 'Temperature';

export type SubScoreKey =
  | 'respirationRate'
  | 'spO2'
  | 'supplementalOxygen'
  | 'systolicBp'
  | 'pulseRate'
  | 'consciousness'
  | 'temperature';

export type Consciousness = 'Alert' | 'Confusion' | 'Voice' | 'Pain' | 'Unresponsive';

export type EventKind =
  | 'Concern'
  | 'Scale2Decision'
  | 'Alert'
  | 'DoctorCalled'
  | 'DoctorArrived'
  | 'TreatmentStarted'
  | 'CriticalCareCalled'
  | 'Note';

export interface News2Input {
  respirationRate: number | null;
  spO2: number | null;
  spO2Scale: 'Scale1' | 'Scale2';
  onSupplementalOxygen: boolean | null;
  systolicBp: number | null;
  pulseRate: number | null;
  temperatureCelsius: number | null;
  consciousness: Consciousness | null;
}

export interface News2Problem {
  parameter: News2Parameter;
  code: 'Missing' | 'ImplausibleValue' | 'Scale2NotAuthorised';
  message: string;
}

export interface ScorePoint {
  at: string;
  total: number | null;
  minimumTotal: number | null;
  risk: RiskLevel | null;
  response: ClinicalResponse | null;
  minimumMonitoringFrequency: string | null;
  subScores: Record<SubScoreKey, number | null>;
  parametersScoring3: News2Parameter[];
  problems: News2Problem[];
  input: News2Input;
  notes: string[];
}

export interface WardEvent {
  at: string;
  kind: EventKind;
  text: string;
}

export type EscalationTier = 'OnCallDoctor' | 'SeniorDoctor' | 'CriticalCare';

export type EscalationStep = 'None' | 'Called' | 'Arrived' | 'Assessed' | 'Planned';

export interface CallState {
  callId: string;
  tier: EscalationTier;
  calledAt: string;
  targetAt: string | null;
  arrivedAt: string | null;
  arrivalSimulated: boolean;
  assessedAt: string | null;
  assessment: string | null;
  plannedAt: string | null;
  plan: string | null;
}

/** The escalation chain at the requested moment, computed by the API (Baton reports; it never calls anyone). */
export interface EscalationState {
  step: EscalationStep;
  concernRaisedAt: string | null;
  concernNote: string | null;
  patientConcernAt: string | null;
  calls: CallState[];
  openCall: CallState | null;
  minutesWaited: number | null;
  targetPassed: boolean;
  suggestedNextTier: EscalationTier | null;
}

export type EscalationRecordKind = 'ConcernRaised' | 'PatientOrFamilyConcern' | 'Called' | 'Arrived' | 'Assessed' | 'Planned';

export interface EscalationRecordView {
  at: string;
  kind: EscalationRecordKind;
  by: string;
  tier: EscalationTier | null;
  text: string | null;
  simulated: boolean | null;
}

export interface BedSummary {
  patientId: string;
  name: string;
  bed: string | null;
  age: number | null;
  diagnosis: string | null;
  latest: ScorePoint | null;
  previous: ScorePoint | null;
  drivers: News2Parameter[];
  nextDue: string | null;
  overdueMinutes: number | null;
  concernsKnown: boolean;
  escalation: EscalationState;
  eventsAvailable: boolean;
  events: WardEvent[];
}

export interface WardResponse {
  at: string;
  source: string;
  shiftStart: string;
  shiftEnd: string;
  beds: BedSummary[];
}

export interface PatientRef {
  id: string;
  name: string;
  bed: string | null;
  spO2Scale: 'Scale1' | 'Scale2';
  birthDate: string | null;
  diagnosis: string | null;
}

export interface PatientSeries {
  patient: PatientRef;
  age: number | null;
  diagnosis: string | null;
  points: ScorePoint[];
  eventsAvailable: boolean;
  events: WardEvent[];
  escalation: EscalationState;
  escalationRecords: EscalationRecordView[];
}

/** "RespirationRate" → "respirationRate", the key of the same parameter in subScores. */
export function subScoreKey(parameter: News2Parameter): SubScoreKey {
  return (parameter.charAt(0).toLowerCase() + parameter.slice(1)) as SubScoreKey;
}
