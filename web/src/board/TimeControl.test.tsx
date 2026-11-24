import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { TimeControl } from './TimeControl';
import { renderIn } from '../test/render';

describe('TimeControl', () => {
  it('shows the board moment, not the rounded slider step', () => {
    renderIn(<TimeControl shiftStart="2026-11-23T08:00:00+03:00" shiftEnd="2026-11-24T08:00:00+03:00" at="2026-11-23T23:38:00+03:00" onChange={vi.fn()} />);

    expect(screen.getByRole('status')).toHaveTextContent('23:38');
  });

  it('stops announcing the clock while the shift plays', () => {
    renderIn(<TimeControl shiftStart="2026-11-23T08:00:00+03:00" shiftEnd="2026-11-24T08:00:00+03:00" at="2026-11-23T23:38:00+03:00" quiet onChange={vi.fn()} />);

    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'off');
  });
});
