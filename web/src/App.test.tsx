import { describe, expect, it, vi } from 'vitest';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { FakeEventSource } from './test/fakeEventSource';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import { bed, noChain, point, ward } from './fixtures';
import { handover, handoverRow } from './handover/fixtures';
import { renderIn } from './test/render';
import type { PatientSeries } from './api/types';

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

const beds = [bed(), bed({ patientId: 'synthetic-05', bed: '05', name: 'Nuri A.', age: 73 })];

function series(id: string, bedNo: string, name: string): PatientSeries {
  return {
    patient: { id, name, bed: bedNo, spO2Scale: 'Scale1', birthDate: null, diagnosis: null },
    age: 70,
    diagnosis: null,
    points: [point()],
    eventsAvailable: true,
    events: [],
    escalation: noChain,
    escalationRecords: [],
  };
}

/** Routes fetch by URL; a route returning undefined never answers. */
function route(routes: Record<string, () => Response | undefined>) {
  const fetchMock = vi.fn((url: string) => {
    const key = Object.keys(routes).find((k) => url.startsWith(k));
    const response = key ? routes[key]!() : respond(404, { title: 'Not found' });
    return response ? Promise.resolve(response) : new Promise<Response>(() => {});
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('App patient panel', () => {
  it('never shows the previous patient while another one loads', async () => {
    route({
      '/api/ward': () => respond(200, ward(beds)),
      '/api/patients/synthetic-02': () => respond(200, series('synthetic-02', '02', 'Ayşe K.')),
      '/api/patients/synthetic-05': () => undefined,
    });
    renderIn(<App />);

    await userEvent.click(await screen.findByRole('button', { name: /Bed 02/ }));
    expect(await screen.findByRole('heading', { name: /Bed 02 · Ayşe K\./ })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /Bed 05/ }));

    await waitFor(() => expect(screen.queryByRole('heading', { name: /Bed 02 · Ayşe K\./ })).toBeNull());
    expect(screen.getByText('Loading the patient…')).toBeInTheDocument();
  });

  it('says when a patient cannot be loaded and retries', async () => {
    let patientCalls = 0;
    route({
      '/api/ward': () => respond(200, ward(beds)),
      '/api/patients/synthetic-05': () => {
        patientCalls += 1;
        return patientCalls === 1
          ? respond(503, { title: 'FHIR server unreachable' })
          : respond(200, series('synthetic-05', '05', 'Nuri A.'));
      },
    });
    renderIn(<App />);

    await userEvent.click(await screen.findByRole('button', { name: /Bed 05/ }));
    expect(await screen.findByText('Could not load this patient.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('heading', { name: /Bed 05 · Nuri A\./ })).toBeInTheDocument();
  });
});

describe('App selection', () => {
  it('brings the patient screen into view when a bed is chosen', async () => {
    route({
      '/api/ward': () => respond(200, ward(beds)),
      '/api/patients/synthetic-02': () => respond(200, series('synthetic-02', '02', 'Ayşe K.')),
    });
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;
    renderIn(<App />);

    await userEvent.click(await screen.findByRole('button', { name: /Bed 02/ }));

    expect(scroll).toHaveBeenCalled();
  });

  it('names the data source in plain words', async () => {
    route({ '/api/ward': () => respond(200, ward(beds)) });
    renderIn(<App />);

    expect(await screen.findByText('Data: synthetic ward, generated in memory')).toBeInTheDocument();
  });
});

describe('App ward errors', () => {
  it('hides cards from an earlier moment when the new moment fails', async () => {
    let wardCalls = 0;
    route({
      '/api/ward': () => {
        wardCalls += 1;
        return wardCalls === 1 ? respond(200, ward(beds)) : respond(503, { title: 'FHIR server unreachable' });
      },
    });
    renderIn(<App />);
    expect(await screen.findByRole('button', { name: /Bed 02/ })).toBeInTheDocument();

    fireEvent.change(screen.getByRole('slider'), { target: { value: '72' } });

    expect(await screen.findByText(/The FHIR server is not answering/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Bed 02/ })).toBeNull();
  });
});

describe('App handover view', () => {
  it('switches to the handover view and loads the list', async () => {
    const fetchMock = route({
      '/health': () => respond(200, { tryAvailable: true }),
      '/api/ward': () => respond(200, ward(beds)),
      '/api/handover': () => respond(200, handover([handoverRow()])),
    });
    renderIn(<App />);

    await userEvent.click(await screen.findByRole('button', { name: 'Handover' }));

    expect(await screen.findByRole('button', { name: /Bed 02 · Ayşe K\./ })).toHaveTextContent('Open SBAR');
    expect(fetchMock.mock.calls.some(([url]) => String(url).startsWith('/api/handover'))).toBe(true);
    expect(window.location.search).toContain('view=handover');
  });
});

describe('App live board', () => {
  it('refetches the ward on a change event and shows the link status', async () => {
    window.history.replaceState(null, '', '/');
    FakeEventSource.install();
    let wardCalls = 0;
    route({
      '/health': () => respond(200, { tryAvailable: true }),
      '/api/ward': () => {
        wardCalls += 1;
        return respond(200, ward(beds));
      },
    });
    renderIn(<App />);
    await screen.findByRole('button', { name: /Bed 02/ });
    const before = wardCalls;

    act(() => FakeEventSource.last().emit('ready', {}));
    expect(screen.getByText('Live link on')).toBeInTheDocument();
    act(() => FakeEventSource.last().emit('change', { kind: 'Fhir', patientId: 'synthetic-02', trySession: null, at: '2026-11-24T08:00:00Z' }));

    await waitFor(() => expect(wardCalls).toBeGreaterThan(before));
  });

  it('pauses the live clock when the slider moves', async () => {
    window.history.replaceState(null, '', '/');
    FakeEventSource.install();
    route({
      '/health': () => respond(200, { tryAvailable: true }),
      '/api/ward': () => respond(200, ward(beds)),
    });
    renderIn(<App />);
    await userEvent.click(await screen.findByRole('button', { name: 'Play the shift' }));
    expect(screen.getByRole('button', { name: 'Pause' })).toBeInTheDocument();

    fireEvent.change(screen.getByRole('slider'), { target: { value: '10' } });

    expect(screen.getByRole('button', { name: 'Play the shift' })).toBeInTheDocument();
  });
});

describe('App live board robustness', () => {
  it('does not move the clock while the ward for the current moment is still loading', async () => {
    window.history.replaceState(null, '', '/?at=2026-11-23T23%3A00%3A00%2B03%3A00');
    FakeEventSource.install();
    let wardCalls = 0;
    route({
      '/health': () => respond(200, { tryAvailable: true }),
      // The first answer comes at once; every later one never does (a slow FHIR server).
      '/api/ward': () => (++wardCalls === 1 ? respond(200, ward(beds)) : undefined),
    });
    renderIn(<App />);
    const play = await screen.findByRole('button', { name: 'Play the shift' });

    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      await userEvent.click(play);
      act(() => vi.advanceTimersByTime(2000));
      const afterFirst = window.location.search;
      expect(afterFirst).toContain('23%3A02');
      act(() => vi.advanceTimersByTime(4000));
      expect(window.location.search).toBe(afterFirst);
    } finally {
      vi.useRealTimers();
    }
  });

  it('refetches once for a burst of change events', async () => {
    window.history.replaceState(null, '', '/');
    FakeEventSource.install();
    let wardCalls = 0;
    route({
      '/health': () => respond(200, { tryAvailable: true }),
      '/api/ward': () => {
        wardCalls += 1;
        return respond(200, ward(beds));
      },
    });
    renderIn(<App />);
    await screen.findByRole('button', { name: /Bed 02/ });
    const before = wardCalls;

    for (let i = 0; i < 7; i++) {
      act(() => FakeEventSource.last().emit('change', { kind: 'Fhir', patientId: 'synthetic-03', trySession: null, at: '2026-11-24T08:00:00Z' }));
    }

    await waitFor(() => expect(wardCalls).toBe(before + 1));
    await new Promise((resolve) => setTimeout(resolve, 500));
    expect(wardCalls).toBe(before + 1);
  });
});

