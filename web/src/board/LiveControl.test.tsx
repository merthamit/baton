import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LiveControl, nextLiveAt } from './LiveControl';
import { renderIn } from '../test/render';

const end = '2026-11-24T08:00:00+03:00';

describe('nextLiveAt', () => {
  it('moves the clock by speed × elapsed time', () => {
    expect(nextLiveAt('2026-11-23T23:30:00+03:00', end, 60, 2000)).toBe('2026-11-23T23:32:00+03:00');
    expect(nextLiveAt('2026-11-23T23:30:00+03:00', end, 1, 2000)).toBe('2026-11-23T23:30:02+03:00');
  });

  it('stops at the shift end', () => {
    expect(nextLiveAt('2026-11-24T07:59:00+03:00', end, 300, 2000)).toBe(end);
  });
});

describe('LiveControl', () => {
  it('plays, pauses and changes speed', async () => {
    const onPlay = vi.fn();
    const onSpeed = vi.fn();
    renderIn(<LiveControl playing={false} speed={60} atEnd={false} onPlay={onPlay} onPause={vi.fn()} onSpeed={onSpeed} />);

    await userEvent.click(screen.getByRole('button', { name: 'Play the shift' }));
    expect(onPlay).toHaveBeenCalled();
    expect(screen.getByRole('button', { name: '60×' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: '300×' }));
    expect(onSpeed).toHaveBeenCalledWith(300);
  });

  it('pauses while playing', async () => {
    const onPause = vi.fn();
    renderIn(<LiveControl playing speed={60} atEnd={false} onPlay={vi.fn()} onPause={onPause} onSpeed={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: 'Pause' }));
    expect(onPause).toHaveBeenCalled();
  });

  it('cannot play at the shift end', () => {
    renderIn(<LiveControl playing={false} speed={60} atEnd onPlay={vi.fn()} onPause={vi.fn()} onSpeed={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Play the shift' })).toBeDisabled();
  });
});
