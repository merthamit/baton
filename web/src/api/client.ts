import type { EscalationTier, HandoverResponse, PatientSeries, WardResponse } from './types';

/** A non-2xx answer from Baton.Api, with the problem details title and detail when there are any. */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail: string | undefined;
  /** The problem details type, e.g. urn:baton:try:no-arrival (a code the UI translates). */
  readonly type: string | undefined;

  constructor(status: number, title: string, detail?: string, type?: string) {
    super(`${status} ${title}`);
    this.name = 'ApiError';
    this.status = status;
    this.title = title;
    this.detail = detail;
    this.type = type;
  }
}

async function send<T>(url: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(url, { ...init, headers: { Accept: 'application/json', ...(init.body ? { 'Content-Type': 'application/json' } : {}) } });
  if (!response.ok) {
    let title = response.statusText;
    let detail: string | undefined;
    let type: string | undefined;
    try {
      const problem = (await response.json()) as { title?: string; detail?: string; type?: string };
      title = problem.title ?? title;
      detail = problem.detail;
      type = problem.type;
    } catch {
      // no problem details body
    }
    throw new ApiError(response.status, title, detail, type);
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

function query(parts: Record<string, string | undefined>): string {
  const q = Object.entries(parts)
    .filter(([, v]) => v)
    .map(([k, v]) => `${k}=${encodeURIComponent(v!)}`)
    .join('&');
  return q ? `?${q}` : '';
}

/**
 * The ward at a moment. `at` must carry its offset, e.g. 2026-11-23T23:30:00+03:00; without it the API's default
 * moment is used and the response says which moment that was. `trySession` lays a Try course over the ward.
 */
export function fetchWard(at: string | undefined, lang?: string, signal?: AbortSignal, trySession?: string): Promise<WardResponse> {
  return send<WardResponse>(`/api/ward${query({ at, lang, try: trySession })}`, { signal });
}

/** One patient's scored series and events up to a moment; `lang` asks for the data's translations ("tr"). */
export function fetchPatient(id: string, at: string, lang?: string, signal?: AbortSignal, trySession?: string): Promise<PatientSeries> {
  return send<PatientSeries>(`/api/patients/${encodeURIComponent(id)}${query({ at, lang, try: trySession })}`, { signal });
}

export interface Health {
  tryAvailable: boolean;
}

export function fetchHealth(signal?: AbortSignal): Promise<Health> {
  return send<Health>('/health', { signal });
}

export interface TryStart {
  sessionId: string;
  patientId: string;
  startedAt: string;
}

export type TryActionKind = 'Concern' | 'PatientOrFamilyConcern' | 'Call' | 'Assessed' | 'Planned';

export interface TryActionBody {
  kind: TryActionKind;
  at: string;
  text?: string;
  tier?: EscalationTier;
}

export function startTry(patientId: string, at: string): Promise<TryStart> {
  return send<TryStart>('/api/try', { method: 'POST', body: JSON.stringify({ patientId, at }) });
}

export function addTryAction(sessionId: string, action: TryActionBody): Promise<{ lastActionAt: string }> {
  return send(`/api/try/${encodeURIComponent(sessionId)}/actions`, { method: 'POST', body: JSON.stringify(action) });
}

export function endTry(sessionId: string): Promise<void> {
  return send<void>(`/api/try/${encodeURIComponent(sessionId)}`, { method: 'DELETE' });
}

/** The ward's handover at a moment, sickest first; `trySession` shows the Try course (it cannot be confirmed). */
export function fetchHandover(at: string, lang?: string, signal?: AbortSignal, trySession?: string): Promise<HandoverResponse> {
  return send<HandoverResponse>(`/api/handover${query({ at, lang, try: trySession })}`, { signal });
}

/** "Taken over": stores the patient's SBAR at this minute as a FHIR document. */
export function confirmHandover(patientId: string, at: string, lang?: string): Promise<{ documentId: string; at: string }> {
  return send(`/api/handover/${encodeURIComponent(patientId)}`, { method: 'POST', body: JSON.stringify({ at, lang }) });
}
