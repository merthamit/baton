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
  /** FHIR references of the readings behind this score (and its NEWS2 Observation when stored). */
  sources: string[];
}

export interface WardEvent {
  at: string;
  kind: EventKind;
  text: string;
}

export type EscalationTier = 'OnCallDoctor' | 'SeniorDoctor' | 'CriticalCare';

export type EscalationStep = 'None' | 'Called' | 'Acknowledged' | 'Arrived' | 'Assessed' | 'Planned';

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
  /** When the called clinician confirmed the call ("coming"); only shift calls have one. */
  acknowledgedAt?: string | null;
  acknowledgedBy?: string | null;
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

export type EscalationRecordKind =
  | 'ConcernRaised'
  | 'PatientOrFamilyConcern'
  | 'Called'
  | 'Acknowledged'
  | 'Arrived'
  | 'Assessed'
  | 'Planned'
  | 'ObservationNotDone';

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
  /** The latest "not done" since the last set (shift mode); urgent when no reading could be obtained. */
  notDone?: NotDoneView | null;
}

export type NotDoneReason = 'PatientAsleep' | 'PatientDeclined' | 'OffTheWard' | 'EquipmentUnavailable' | 'EndOfLifeCare' | 'CouldNotBeObtained';

export interface NotDoneView {
  at: string;
  reason: NotDoneReason;
  note: string | null;
  urgent: boolean;
}

// Real ward flow (week 10): /api/shift.

export interface ShiftState {
  shiftId: string;
  startedAt: string;
  now: string;
  speed: number;
  shiftEnd: string;
}

/** One bedside set; oxygen is a flow in L/min, 0 = room air. */
export interface VitalSet {
  respirationRate: number;
  spO2: number;
  oxygenLitresPerMinute: number;
  systolicBp: number;
  pulseRate: number;
  temperatureCelsius: number;
  consciousness: Consciousness;
}

export interface MeasureResult {
  at: string;
  vitals: VitalSet;
  scale: 'Scale1' | 'Scale2';
}

export interface ShiftObservationResult {
  latest: ScorePoint | null;
  nextDue: string | null;
  escalation: EscalationState;
}

/** The engine's score for a set that is not stored (the entry form's preview). */
export interface News2Preview {
  isComplete: boolean;
  total: number | null;
  risk: RiskLevel | null;
  response: ClinicalResponse | null;
  subScores: Record<SubScoreKey, number | null>;
  parametersScoring3: News2Parameter[];
}

export interface DoctorCall {
  patientId: string;
  bed: string | null;
  name: string;
  latest: ScorePoint | null;
  step: EscalationStep;
  call: CallState;
  minutesSinceCall: number;
  targetPassed: boolean;
  suggestedNextTier: EscalationTier | null;
}

export type ShiftActionKind = 'Concern' | 'PatientOrFamilyConcern' | 'Call' | 'Acknowledge' | 'Arrive' | 'Assess' | 'Plan';

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

// SBAR handover (week 8): /api/handover.

export type SbarPart = 'Situation' | 'Background' | 'Assessment' | 'Recommendation';

export type MissingKind =
  | 'NoObservations'
  | 'NoCallAfterScore'
  | 'NoCriticalCareCall'
  | 'NoArrival'
  | 'NoAssessment'
  | 'NoPlan'
  | 'ObservationOverdue';

export interface SourceRef {
  reference: string;
  at: string;
  label: string;
}

export interface SbarSentence {
  text: string;
  sources: SourceRef[];
}

export interface SbarSection {
  part: SbarPart;
  sentences: SbarSentence[];
}

export interface MissingItem {
  kind: MissingKind;
  text: string;
  sources: SourceRef[];
}

/** One patient's rule-based SBAR, rendered by the API in the requested language. */
export interface Sbar {
  at: string;
  windowStart: string;
  language: string;
  sections: SbarSection[];
  missing: MissingItem[];
  stateLine: string;
  currentTotal: number | null;
  currentRisk: RiskLevel | null;
  peakTotal: number | null;
  /** The risk an incomplete set already shows (the API sorts by it); equals currentRisk when scored. */
  floorRisk: RiskLevel | null;
}

export interface ConfirmedHandover {
  documentId: string;
  at: string;
}

export interface HandoverRow {
  patientId: string;
  bed: string | null;
  name: string;
  sbar: Sbar;
  confirmed: ConfirmedHandover | null;
  newDataSinceHandover: boolean;
}

/** The ward's handover at a moment, sickest first (the API orders the rows). */
export interface HandoverResponse {
  at: string;
  windowStart: string;
  canConfirm: boolean;
  confirmedCount: number;
  rows: HandoverRow[];
}
