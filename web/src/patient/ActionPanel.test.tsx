import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ActionPanel } from './ActionPanel';
import { call, chain, noChain } from '../fixtures';
import { renderIn } from '../test/render';

const base = {
  patientName: 'Ayşe K.',
  bedLabel: 'Bed 02',
  at: '2026-11-24T01:30:00+03:00',
  lastActionAt: '2026-11-23T22:00:00+03:00',
};

describe('ActionPanel', () => {
  it('opens a dialog naming the patient and records a call with the chosen tier', async () => {
    const onAction = vi.fn().mockResolvedValue(null);
    renderIn(<ActionPanel {...base} state={noChain} onAction={onAction} />);

    await userEvent.click(screen.getByRole('button', { name: 'Call doctor' }));
    expect(screen.getByRole('dialog', { name: 'Call doctor — Bed 02 · Ayşe K.' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));

    expect(onAction).toHaveBeenCalledWith('Call', { text: '', tier: 'OnCallDoctor' });
  });

  it('disables every action before the last one, with the reason', () => {
    renderIn(<ActionPanel {...base} at="2026-11-23T21:00:00+03:00" state={noChain} onAction={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'I am concerned' })).toBeDisabled();
    expect(screen.getByText('Move the clock to 22:00 or later to record.')).toBeInTheDocument();
  });

  it('keeps the assessment disabled until the doctor has arrived', () => {
    const waiting = chain({ calls: [call({ calledAt: '2026-11-24T01:00:00+03:00' })] });
    renderIn(<ActionPanel {...base} state={waiting} onAction={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Record assessment' })).toBeDisabled();
    expect(screen.getByText('The doctor has not arrived yet.')).toBeInTheDocument();
  });

  it('enables the assessment once the doctor is there, and the plan after the assessment', () => {
    const arrived = call({ calledAt: '2026-11-24T01:00:00+03:00', arrivedAt: '2026-11-24T01:21:00+03:00', arrivalSimulated: true });
    renderIn(<ActionPanel {...base} state={chain({ step: 'Arrived', calls: [arrived], openCall: null })} onAction={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Record assessment' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Record plan' })).toBeDisabled();
  });

  it('offers the suggested tier as its own action', async () => {
    const onAction = vi.fn().mockResolvedValue(null);
    renderIn(<ActionPanel {...base} state={chain({ suggestedNextTier: 'CriticalCare' })} onAction={onAction} />);

    await userEvent.click(screen.getByRole('button', { name: 'Call critical care' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));

    expect(onAction).toHaveBeenCalledWith('Call', { text: '', tier: 'CriticalCare' });
  });

  it('shows the API reason when an action is refused', async () => {
    const onAction = vi.fn().mockResolvedValue('Record the assessment before the plan.');
    renderIn(<ActionPanel {...base} state={noChain} onAction={onAction} />);

    await userEvent.click(screen.getByRole('button', { name: 'I am concerned' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByText('Record the assessment before the plan.')).toBeInTheDocument();
  });
});
