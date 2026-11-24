import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { WardBoard } from './WardBoard';
import { App } from '../App';
import { bed, quietBed, ward } from '../fixtures';
import { renderIn } from '../test/render';

describe('WardBoard', () => {
  it('renders thirty beds in bed order', () => {
    const beds = Array.from({ length: 30 }, (_, i) => quietBed(30 - i));
    renderIn(<WardBoard ward={ward(beds)} selectedId={undefined} onSelect={vi.fn()} />);

    const cards = within(screen.getByRole('region', { name: 'Beds' })).getAllByRole('button');
    expect(cards).toHaveLength(30);
    expect(cards[0]).toHaveAccessibleName(/Bed 01/);
    expect(cards[29]).toHaveAccessibleName(/Bed 30/);
  });

  it('shows an empty state for a ward without patients', () => {
    renderIn(<WardBoard ward={ward([])} selectedId={undefined} onSelect={vi.fn()} />);
    expect(screen.getByText(/No patients on this ward/)).toBeInTheDocument();
  });

  it('marks the selected bed as pressed', () => {
    renderIn(<WardBoard ward={ward([bed(), quietBed(3)])} selectedId="synthetic-02" onSelect={vi.fn()} />);
    expect(screen.getByRole('button', { name: /Bed 02/ })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: /Bed 03/ })).toHaveAttribute('aria-pressed', 'false');
  });
});

describe('App states', () => {
  function respond(status: number, body: unknown) {
    return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
  }

  it('shows the synthetic-data notice in both languages', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respond(200, ward([bed()]))));
    renderIn(<App />);
    expect(screen.getByText('Synthetic data · not a medical device')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Türkçe' }));
    expect(screen.getByText('Sentetik veri · tıbbi cihaz değildir')).toBeInTheDocument();
  });

  it('explains a FHIR server that is down and retries', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(respond(503, { title: 'FHIR server unreachable' }))
      .mockResolvedValue(respond(200, ward([bed()])));
    vi.stubGlobal('fetch', fetchMock);
    renderIn(<App />);

    expect(await screen.findByText(/The FHIR server is not answering/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(screen.getByRole('button', { name: /Bed 02/ })).toBeInTheDocument());
  });
});
