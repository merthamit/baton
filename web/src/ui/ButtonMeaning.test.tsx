import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderIn } from '../test/render';
import { noChain } from '../fixtures';
import type { ShiftState } from '../api/types';
import { ConfirmDialog } from './ConfirmDialog';
import { ActionPanel } from '../patient/ActionPanel';
import { ShiftBar } from '../shift/ShiftBar';

// A button's colour says what it does: blue goes on, green confirms, orange escalates or raises a concern,
// red ends something, grey cancels.
describe('button meaning', () => {
  it('confirms in green, confirms a call in orange and confirms ending in red', () => {
    const { unmount } = renderIn(<ConfirmDialog title="I am concerned" onConfirm={vi.fn()} onCancel={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Confirm' })).toHaveClass('btn', 'confirm');
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveClass('btn');
    unmount();

    const call = renderIn(<ConfirmDialog title="Call doctor" withTier onConfirm={vi.fn()} onCancel={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Confirm' })).toHaveClass('escalate');
    call.unmount();

    renderIn(<ConfirmDialog title="End shift?" tone="danger" onConfirm={vi.fn()} onCancel={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Confirm' })).toHaveClass('danger');
  });

  it('marks concerns and the call to the doctor in orange', () => {
    renderIn(
      <ActionPanel
        patientName="Ayşe K."
        bedLabel="Bed 02"
        at="2026-11-24T01:30:00+03:00"
        lastActionAt="2026-11-23T22:00:00+03:00"
        state={noChain}
        onAction={vi.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: 'I am concerned' })).toHaveClass('concern');
    expect(screen.getByRole('button', { name: 'Patient or family is worried' })).toHaveClass('concern');
    expect(screen.getByRole('button', { name: 'Call doctor' })).toHaveClass('escalate');
  });

  it('shows ending the shift in red, and its confirmation too', async () => {
    const state: ShiftState = {
      shiftId: 'a'.repeat(32),
      startedAt: '2026-11-23T22:00:00+03:00',
      now: '2026-11-23T22:30:00+03:00',
      speed: 1,
      shiftEnd: '2026-11-24T08:00:00+03:00',
    };
    renderIn(<ShiftBar state={state} fetchedAt={Date.now()} role="nurse" otherRoleHref="/" onRole={() => {}} onSpeed={() => {}} onEnd={vi.fn()} />);

    const end = screen.getByRole('button', { name: 'End shift' });
    expect(end).toHaveClass('danger-outline');
    await userEvent.click(end);

    expect(screen.getByRole('button', { name: 'Confirm' })).toHaveClass('danger');
  });
});
