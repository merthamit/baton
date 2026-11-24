import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../App';
import { bed, noChain, point, ward } from '../fixtures';
import { renderIn } from '../test/render';
import type { PatientSeries } from '../api/types';

function respond(status: number, body: unknown): Response {
  return new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

const series: PatientSeries = {
  patient: { id: 'synthetic-02', name: 'Ayşe K.', bed: '02', spO2Scale: 'Scale1', birthDate: null, diagnosis: null },
  age: 71,
  diagnosis: null,
  points: [point()],
  eventsAvailable: true,
  events: [],
  escalation: noChain,
  escalationRecords: [],
};

type Handler = (url: string, init?: RequestInit) => Response | undefined;

function route(handler: Handler) {
  const fetchMock = vi.fn((url: string, init?: RequestInit) => {
    const response = handler(url, init) ?? respond(404, { title: 'Not found' });
    return Promise.resolve(response);
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function api(tryAvailable = true, wardWithTry: (url: string) => Response = () => respond(200, ward([bed()]))): Handler {
  return (url, init) => {
    if (url.startsWith('/health')) return respond(200, { tryAvailable });
    if (url === '/api/try' && init?.method === 'POST') return respond(201, { sessionId: 's1', patientId: 'synthetic-02', startedAt: '2026-11-23T23:30:00+03:00' });
    if (url.startsWith('/api/try/s1') && init?.method === 'DELETE') return respond(204, null);
    if (url.startsWith('/api/ward')) return url.includes('try=') ? wardWithTry(url) : respond(200, ward([bed()]));
    if (url.startsWith('/api/patients/synthetic-02')) return respond(200, series);
    return undefined;
  };
}

describe('Try mode', () => {
  it('is disabled with the reason when the data source cannot run it', async () => {
    route(api(false));
    renderIn(<App />);

    const button = await screen.findByRole('button', { name: 'Try' });
    await waitFor(() => expect(button).toBeDisabled());
    expect(await screen.findByText('Try needs the in-memory data.')).toBeInTheDocument();
  });

  it('needs a bed to be chosen first', async () => {
    route(api());
    renderIn(<App />);

    const button = await screen.findByRole('button', { name: 'Try' });
    await waitFor(() => expect(screen.getByText('Choose a bed first.')).toBeInTheDocument());
    expect(button).toBeDisabled();
  });

  it('starts for the chosen bed, shows the banner and the note, and Reset ends it', async () => {
    const fetchMock = route(api());
    renderIn(<App />);

    await userEvent.click(await screen.findByRole('button', { name: /Bed 02/ }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Try' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Try' }));

    expect(await screen.findByText(/Try: Bed 02 · Ayşe K\./)).toBeInTheDocument();
    expect(screen.getByText(/Calling earlier than the scenario/)).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('try=s1'))).toBe(true);

    await userEvent.click(screen.getByRole('button', { name: 'Reset' }));
    await waitFor(() => expect(screen.queryByText(/Try: Bed 02/)).toBeNull());
    expect(fetchMock.mock.calls.some(([url, init]) => String(url) === '/api/try/s1' && init?.method === 'DELETE')).toBe(true);
  });

  it('never restarts Try silently when the pressed Try button is clicked again', async () => {
    const fetchMock = route(api());
    renderIn(<App />);

    await userEvent.click(await screen.findByRole('button', { name: /Bed 02/ }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Try' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Try' }));
    await screen.findByText(/Try: Bed 02/);
    await userEvent.click(screen.getByRole('button', { name: 'Try' }));

    const starts = fetchMock.mock.calls.filter(([url, init]) => String(url) === '/api/try' && init?.method === 'POST');
    expect(starts).toHaveLength(1);
  });

  it('returns to Watch with a message when the session has expired', async () => {
    route(api(true, () => respond(404, { title: 'Try session expired' })));
    renderIn(<App />);

    await userEvent.click(await screen.findByRole('button', { name: /Bed 02/ }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Try' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Try' }));

    expect(await screen.findByText('The Try session ended. Start Try again.')).toBeInTheDocument();
    expect(screen.queryByText(/Try: Bed 02/)).toBeNull();
  });
});
