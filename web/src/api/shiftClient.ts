import { send } from './client';
import type {
  DoctorCall,
  EscalationTier,
  MeasureResult,
  News2Preview,
  NotDoneReason,
  ShiftActionKind,
  ShiftObservationResult,
  ShiftState,
  VitalSet,
} from './types';

// The real ward flow (decisions 19–26): a private shift with a running clock, bedside entry and the call loop.

const base = (id: string) => `/api/shift/${encodeURIComponent(id)}`;

export function startShift(at: string, speed: number): Promise<ShiftState> {
  return send<ShiftState>('/api/shift', { method: 'POST', body: JSON.stringify({ at, speed }) });
}

export function fetchShift(id: string, signal?: AbortSignal): Promise<ShiftState> {
  return send<ShiftState>(base(id), { signal });
}

export function setShiftSpeed(id: string, speed: number): Promise<ShiftState> {
  return send<ShiftState>(`${base(id)}/speed`, { method: 'PUT', body: JSON.stringify({ speed }) });
}

export function endShift(id: string): Promise<void> {
  return send<void>(base(id), { method: 'DELETE' });
}

export function measure(id: string, patientId: string): Promise<MeasureResult> {
  return send<MeasureResult>(`${base(id)}/patients/${encodeURIComponent(patientId)}/measure`);
}

export function saveObservation(id: string, patientId: string, vitals: VitalSet): Promise<ShiftObservationResult> {
  return send<ShiftObservationResult>(`${base(id)}/observations`, { method: 'POST', body: JSON.stringify({ patientId, vitals }) });
}

export function saveNotDone(id: string, patientId: string, reason: NotDoneReason, note?: string): Promise<ShiftState> {
  return send<ShiftState>(`${base(id)}/not-done`, { method: 'POST', body: JSON.stringify({ patientId, reason, note }) });
}

export function shiftAction(id: string, patientId: string, kind: ShiftActionKind, text?: string, tier?: EscalationTier): Promise<ShiftState> {
  return send<ShiftState>(`${base(id)}/actions`, { method: 'POST', body: JSON.stringify({ patientId, kind, text, tier }) });
}

export function fetchCalls(id: string, lang?: string, signal?: AbortSignal): Promise<{ now: string; calls: DoctorCall[] }> {
  return send(`${base(id)}/calls${lang ? `?lang=${encodeURIComponent(lang)}` : ''}`, { signal });
}

/** Scores a set without storing it, with the patient's SpO2 scale and Scale 2 decision as at the shift's now. */
export function previewScore(vitals: VitalSet, patientId: string, shift: string, signal?: AbortSignal): Promise<News2Preview> {
  return send<News2Preview>('/api/news2', { method: 'POST', body: JSON.stringify({ vitals, patientId, shift }), signal });
}
