import { describe, expect, it, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SbarPanel } from './SbarPanel';
import { handoverRow, sbar } from './fixtures';
import { renderIn } from '../test/render';

describe('SbarPanel', () => {
  it('renders four sections with a source button after every sentence', () => {
    renderIn(<SbarPanel row={handoverRow()} canConfirm onConfirm={vi.fn()} />);

    expect(screen.getByRole('heading', { name: 'Bed 02 · Ayşe K. — SBAR' })).toBeInTheDocument();
    for (const name of ['Situation', 'Background', 'Assessment', 'Recommendation']) {
      const section = screen.getByRole('region', { name });
      const items = within(section).getAllByRole('listitem');
      for (const item of items) {
        expect(within(item).getByRole('button', { name: /Source/ })).toBeInTheDocument();
      }
    }
    expect(screen.getByRole('region', { name: 'Missing from the record' })).toHaveTextContent('No plan recorded for the on-call doctor call');
  });

  it('confirms through the dialog naming bed and patient', async () => {
    const onConfirm = vi.fn().mockResolvedValue(null);
    renderIn(<SbarPanel row={handoverRow()} canConfirm onConfirm={onConfirm} />);

    await userEvent.click(screen.getByRole('button', { name: 'Taken over' }));
    expect(screen.getByRole('dialog', { name: 'Taken over — Bed 02 · Ayşe K.' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('shows the reason when the API refuses', async () => {
    renderIn(<SbarPanel row={handoverRow()} canConfirm onConfirm={vi.fn().mockResolvedValue('Already taken over')} />);

    await userEvent.click(screen.getByRole('button', { name: 'Taken over' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Already taken over');
  });

  it('hides Taken over in Try and explains why', () => {
    renderIn(<SbarPanel row={handoverRow()} canConfirm={false} onConfirm={vi.fn()} />);

    expect(screen.queryByRole('button', { name: 'Taken over' })).toBeNull();
    expect(screen.getByText('Try mode is a rehearsal: handovers are not stored.')).toBeInTheDocument();
  });

  it('shows an existing confirmation at this moment instead of the button', () => {
    const row = handoverRow({ confirmed: { documentId: 'handover-synthetic-02-202611240800', at: '2026-11-24T08:00:00+03:00' }, sbar: sbar() });
    renderIn(<SbarPanel row={row} canConfirm onConfirm={vi.fn()} />);

    expect(screen.queryByRole('button', { name: 'Taken over' })).toBeNull();
    expect(screen.getByRole('status')).toHaveTextContent('Taken over 08:00');
  });

  it('uses Turkish section names', () => {
    renderIn(<SbarPanel row={handoverRow()} canConfirm onConfirm={vi.fn()} />, 'tr');

    expect(screen.getByRole('region', { name: 'Durum' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Devraldım' })).toBeInTheDocument();
  });
});

describe('SbarPanel confirmation to the minute', () => {
  it('treats a handover stored at the minute as this moment', () => {
    const row = handoverRow({
      confirmed: { documentId: 'handover-synthetic-02-202611240800', at: '2026-11-24T08:00:00+03:00' },
      sbar: sbar({ at: '2026-11-24T08:00:40+03:00' }),
    });
    renderIn(<SbarPanel row={row} canConfirm onConfirm={vi.fn()} />);

    expect(screen.queryByRole('button', { name: 'Taken over' })).toBeNull();
  });
});
