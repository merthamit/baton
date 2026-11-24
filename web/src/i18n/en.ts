// English interface text (the default). tr.ts must have exactly the same keys and signatures: a missing or extra
// key is a TypeScript error. Only the interface is translated; clinical free text from the data stays as stored.

export const en = {
  langName: 'English',
  appName: 'Baton',
  wardName: 'Medical ward 3',
  boardTitle: 'Ward board',
  syntheticNotice: 'Synthetic data · not a medical device',
  syntheticDetail: 'All patients are invented. Baton must not be used for patient care.',
  languageLabel: 'Language',

  timeLabel: 'Ward time',
  timeHint: 'Cards show what the nurse sees at this moment: last observations, who was called, the next set due.',
  dataSource: (source: string) => `Data source: ${source}`,

  bed: (bed: string) => `Bed ${bed}`,
  age: (years: number) => `${years}`,
  boardLabel: 'Beds',

  riskLow: 'Low',
  riskLowMedium: 'Low–medium',
  riskMedium: 'Medium',
  riskHigh: 'High',

  paramRespirationRate: 'Resp',
  paramSpO2: 'SpO₂',
  paramSupplementalOxygen: 'Oxygen',
  paramSystolicBp: 'Systolic',
  paramPulseRate: 'Pulse',
  paramConsciousness: 'ACVPU',
  paramTemperature: 'Temp',
  onOxygen: 'on O₂',
  roomAir: 'air',

  raisedBy: 'Raised by',
  allNormal: 'All parameters normal',
  noObservations: 'No observations yet',
  missing: (parameter: string) => `${parameter} missing`,
  notScored: (parameter: string) => `${parameter} not scored`,
  atLeast: (n: number) => `at least ${n}`,
  incompleteScore: 'Incomplete score',

  trendUp: (change: number, hours: string) => `↑ +${change} / ${hours} h`,
  trendDown: (change: number, hours: string) => `↓ ${change} / ${hours} h`,
  trendFlat: '→ no change',

  nextObs: (time: string) => `Next obs ${time}`,
  overdue: (minutes: number) => `Overdue ${minutes} min`,
  continuous: 'Continuous monitoring',
  lastObs: (time: string) => `Last obs ${time}`,

  concern: (time: string) => `Concern ${time}`,
  doctorCalledWaiting: (time: string, minutes: number) => `Doctor called ${time} · waiting ${minutes} min`,
  doctorArrived: (time: string) => `Doctor arrived ${time}`,
  criticalCare: (time: string) => `Critical care called ${time}`,
  scale2: 'SpO₂ Scale 2',
  eventsUnavailable: 'Escalation events are not available from this data source yet',

  loading: 'Loading the ward…',
  loadError: 'Could not load the ward.',
  fhirDown: 'The FHIR server is not answering. Start it, then retry.',
  retry: 'Retry',
  emptyWard: 'No patients on this ward. Load the synthetic ward, then retry.',

  eventConcern: 'Concern flag',
  eventScale2Decision: 'Scale 2 decision',
  eventAlert: 'Baton alert',
  eventDoctorCalled: 'Doctor called',
  eventDoctorArrived: 'Doctor arrived',
  eventTreatmentStarted: 'Treatment started',
  eventCriticalCareCalled: 'Critical care called',
  eventNote: 'Note',

  selectHint: 'Select a bed to see the patient’s chart, events and observations.',
  patientError: 'Could not load this patient.',
  patientLoading: 'Loading the patient…',
  chartLabel: (bed: string, from: string, to: string) => `NEWS2 over the shift for ${bed}, ${from} to ${to}`,
  events: 'Events',
  noEvents: 'No events this shift.',
  observations: 'Observations',
  colTime: 'Time',
  colTotal: 'NEWS2',
  scale2Short: 'S2',
  beforeShift: 'before',
  scenarioDiagnosis: (diagnosis: string) => diagnosis,
};

export type Messages = typeof en;
