import { describe, expect, it, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { ApiError, fetchPatient, fetchWard } from './client';
import { useLatest } from './useLatest';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status === 200 ? 'application/json' : 'application/problem+json' },
  });
}

describe('API client', () => {
  it('sends the time with its offset, URL-encoded', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ beds: [] }));
    vi.stubGlobal('fetch', fetchMock);

    await fetchWard('2026-11-23T23:30:00+03:00');

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/ward?at=2026-11-23T23%3A30%3A00%2B03%3A00');
  });

  it('asks for the data language', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse({ beds: [] })));
    vi.stubGlobal('fetch', fetchMock);

    await fetchWard('2026-11-23T23:30:00+03:00', 'tr');
    await fetchPatient('synthetic-02', '2026-11-23T23:30:00+03:00', 'tr');

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/ward?at=2026-11-23T23%3A30%3A00%2B03%3A00&lang=tr');
    expect(fetchMock.mock.calls[1]?.[0]).toBe('/api/patients/synthetic-02?at=2026-11-23T23%3A30%3A00%2B03%3A00&lang=tr');
  });

  it('turns a 503 problem into an ApiError with status and title', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'FHIR server unreachable' }, 503)));

    const error = await fetchWard('2026-11-23T23:30:00+03:00').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(503);
    expect((error as ApiError).title).toBe('FHIR server unreachable');
  });
});

describe('useLatest', () => {
  it('keeps the newest result when an older request finishes later', async () => {
    const resolvers: Record<string, (value: string) => void> = {};
    const load = (key: string) => () => new Promise<string>((resolve) => (resolvers[key] = resolve));

    const { result, rerender } = renderHook(({ key }) => useLatest(load(key), [key]), {
      initialProps: { key: 'old' },
    });
    rerender({ key: 'new' });

    await act(async () => resolvers.new?.('new data'));
    await act(async () => resolvers.old?.('old data'));

    await waitFor(() => expect(result.current.data).toBe('new data'));
    expect(result.current.loading).toBe(false);
  });

  it('exposes the error and retries on request', async () => {
    let calls = 0;
    const load = () => {
      calls += 1;
      return calls === 1 ? Promise.reject(new ApiError(503, 'FHIR server unreachable')) : Promise.resolve('ok');
    };

    const { result } = renderHook(() => useLatest(load, []));
    await waitFor(() => expect(result.current.error).toBeInstanceOf(ApiError));

    act(() => result.current.retry());

    await waitFor(() => expect(result.current.data).toBe('ok'));
    expect(result.current.error).toBeUndefined();
  });
});
