import type { PatientSeries, WardResponse } from './types';

/** A non-2xx answer from Baton.Api, with the problem details title when there is one. */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;

  constructor(status: number, title: string) {
    super(`${status} ${title}`);
    this.name = 'ApiError';
    this.status = status;
    this.title = title;
  }
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal, headers: { Accept: 'application/json' } });
  if (!response.ok) {
    let title = response.statusText;
    try {
      const problem = (await response.json()) as { title?: string };
      title = problem.title ?? title;
    } catch {
      // no problem details body
    }
    throw new ApiError(response.status, title);
  }
  return (await response.json()) as T;
}

/** The ward at a moment. `at` must carry its offset, e.g. 2026-11-23T23:30:00+03:00. */
export function fetchWard(at: string, signal?: AbortSignal): Promise<WardResponse> {
  return getJson<WardResponse>(`/api/ward?at=${encodeURIComponent(at)}`, signal);
}

/** One patient's scored series and events up to a moment. */
export function fetchPatient(id: string, at: string, signal?: AbortSignal): Promise<PatientSeries> {
  return getJson<PatientSeries>(`/api/patients/${encodeURIComponent(id)}?at=${encodeURIComponent(at)}`, signal);
}
