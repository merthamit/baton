import { describe, expect, it, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HandoverList } from './HandoverList';
import { handover, handoverRow, sbar } from './fixtures';
import { renderIn } from '../test/render';

const stable = handoverRow({
  patientId: 'synthetic-01',
  bed: '01',
  name: 'Mehmet D.',
  sbar: sbar({ currentRisk: 'Low', currentTotal: 0, peakTotal: 0, missing: [], stateLine: 'Stable' }),
});
const sick = handoverRow({
  patientId: 'synthetic-04',
  bed: '04',
  name: 'Hasan Y.',
  sbar: sbar({ currentRisk: 'High', currentTotal: 8, peakTotal: 8, missing: [], stateLine: 'Critical care team arrived 01:30' }),
});

describe('HandoverList', () => {
  it('keeps the server order and shows the confirmed count', () => {
    const rows = [sick, handoverRow(), stable];
    renderIn(<HandoverList response={handover(rows, { confirmedCount: 1 })} selectedId={undefined} onSelect={vi.fn()} />);

    const buttons = screen.getAllByRole('button');
    expect(buttons.map((b) => within(b).getByText(/Bed \d\d/).textContent)).toEqual(['Bed 04 · Hasan Y.', 'Bed 02 · Ayşe K.', 'Bed 01 · Mehmet D.']);
    expect(screen.getByText('1 / 3 taken over')).toBeInTheDocument();
  });

  it('shows missing items as text, not colour only', () => {
    renderIn(<HandoverList response={handover([handoverRow()])} selectedId={undefined} onSelect={vi.fn()} />);

    expect(screen.getByText('No plan recorded for the on-call doctor call')).toBeInTheDocument();
    expect(screen.getByText('On-call doctor assessed 23:45')).toBeInTheDocument();
    expect(screen.getByText('Open SBAR')).toBeInTheDocument();
  });

  it('marks a confirmed row with its time and new-data note', () => {
    const row = handoverRow({ confirmed: { documentId: 'handover-synthetic-02-202611240600', at: '2026-11-24T06:00:00+03:00' }, newDataSinceHandover: true });
    renderIn(<HandoverList response={handover([row])} selectedId={undefined} onSelect={vi.fn()} />);

    expect(screen.getByText(/Taken over 06:00/)).toBeInTheDocument();
    expect(screen.getByText(/new data since handover/)).toBeInTheDocument();
  });

  it('selects a patient', async () => {
    const onSelect = vi.fn();
    renderIn(<HandoverList response={handover([handoverRow()])} selectedId="synthetic-02" onSelect={onSelect} />);

    const button = screen.getByRole('button', { name: /Bed 02/ });
    expect(button).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(button);
    expect(onSelect).toHaveBeenCalledWith('synthetic-02');
  });
});
