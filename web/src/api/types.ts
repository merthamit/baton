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

export interface Escalation {
  concernRaisedAt: string | null;
  doctorCalledAt: string | null;
  doctorArrivedAt: string | null;
  criticalCareCalledAt: string | null;
  waitingMinutes: number | null;
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
  escalation: Escalation;
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
}

/** "RespirationRate" → "respirationRate", the key of the same parameter in subScores. */
export function subScoreKey(parameter: News2Parameter): SubScoreKey {
  return (parameter.charAt(0).toLowerCase() + parameter.slice(1)) as SubScoreKey;
}
