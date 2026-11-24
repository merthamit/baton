import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SourceButton } from './SourceButton';
import { renderIn } from '../test/render';

const sources = [
  { reference: 'Observation/obs-1-rr', at: '2026-11-24T07:00:00+03:00', label: 'Observations 07:00' },
  { reference: 'Observation/obs-1-hr', at: '2026-11-24T07:00:00+03:00', label: 'Observations 07:00' },
  { reference: 'Flag/concern-1', at: '2026-11-23T20:15:00+03:00', label: 'Nurse concern 20:15' },
];

describe('SourceButton', () => {
  it('is described by the sentence it belongs to', () => {
    renderIn(
      <p>
        <span id="s1">NEWS2 3 Low at 07:00.</span> <SourceButton sources={sources} describedBy="s1" />
      </p>,
    );

    expect(screen.getByRole('button', { name: 'Source' })).toHaveAccessibleDescription('NEWS2 3 Low at 07:00.');
  });

  it('closes when you click elsewhere', async () => {
    renderIn(
      <div>
        <SourceButton sources={sources} />
        <p>Elsewhere</p>
      </div>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Source' }));
    expect(screen.getByRole('group', { name: 'Sources of this sentence' })).toBeInTheDocument();

    await userEvent.click(screen.getByText('Elsewhere'));

    expect(screen.queryByRole('group', { name: 'Sources of this sentence' })).toBeNull();
  });

  it('opens and closes the source popover with Esc, returning focus', async () => {
    renderIn(<SourceButton sources={sources} />);
    const button = screen.getByRole('button', { name: 'Source' });
    expect(button).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(button);
    expect(button).toHaveAttribute('aria-expanded', 'true');
    const group = screen.getByRole('group', { name: 'Sources of this sentence' });
    expect(group).toHaveTextContent('Observations 07:00');
    expect(group).toHaveTextContent('Observation/obs-1-hr');
    expect(group).toHaveTextContent('Flag/concern-1');
    expect(screen.getAllByText('Observations 07:00')).toHaveLength(1);

    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('group', { name: 'Sources of this sentence' })).toBeNull();
    expect(button).toHaveFocus();
  });
});
