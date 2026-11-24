import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfirmDialog } from './ConfirmDialog';
import { renderIn } from '../test/render';

describe('ConfirmDialog', () => {
  it('names the action, the bed and the patient in its title', () => {
    renderIn(<ConfirmDialog title="Call doctor — Bed 02 · Ayşe K." onConfirm={vi.fn()} onCancel={vi.fn()} />);
    expect(screen.getByRole('dialog', { name: 'Call doctor — Bed 02 · Ayşe K.' })).toBeInTheDocument();
  });

  it('sends the note and the chosen tier on confirm', async () => {
    const onConfirm = vi.fn();
    renderIn(<ConfirmDialog title="Call doctor — Bed 02 · Ayşe K." withNote withTier onConfirm={onConfirm} onCancel={vi.fn()} />);

    await userEvent.type(screen.getByRole('textbox', { name: 'Note' }), 'Septic picture');
    await userEvent.click(screen.getByRole('radio', { name: 'Critical care' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));

    expect(onConfirm).toHaveBeenCalledWith({ text: 'Septic picture', tier: 'CriticalCare' });
  });

  it('cancels with Escape', async () => {
    const onCancel = vi.fn();
    renderIn(<ConfirmDialog title="I am concerned — Bed 02 · Ayşe K." withNote onConfirm={vi.fn()} onCancel={onCancel} />);

    await userEvent.keyboard('{Escape}');

    expect(onCancel).toHaveBeenCalled();
  });
});
