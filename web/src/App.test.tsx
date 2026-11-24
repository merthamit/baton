import { describe, expect, it, vi } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import { bed, noChain, point, ward } from './fixtures';
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
