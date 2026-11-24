import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/react';
import { ChainStrip } from './ChainStrip';
import { call, chain, noChain } from '../fixtures';
import { renderIn } from '../test/render';

describe('ChainStrip', () => {
  it('renders nothing before any escalation', () => {
    const { container } = renderIn(<ChainStrip state={noChain} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows the four steps as text, the called step reached, and the timer', () => {
    renderIn(<ChainStrip state={chain()} />);
    const steps = within(screen.getByRole('list', { name: 'Escalation' })).getAllByRole('listitem');

    expect(steps.map((s) => s.textContent)).toEqual([
      expect.stringContaining('Called'),
      expect.stringContaining('Arrived'),
      expect.stringContaining('Assessed'),
      expect.stringContaining('Plan'),
    ]);
    expect(steps[0]).toHaveAttribute('data-state', 'done');
    expect(steps[1]).toHaveAttribute('data-state', 'todo');
    expect(screen.getByText('On-call doctor 23:19')).toBeInTheDocument();
    expect(screen.getByText('11 / 60 min')).toBeInTheDocument();
  });

  it('marks every step done once there is a plan', () => {
    const planned = call({ arrivedAt: '2026-11-23T23:40:00+03:00', assessedAt: '2026-11-23T23:40:00+03:00', plannedAt: '2026-11-24T00:05:00+03:00' });
    renderIn(<ChainStrip state={chain({ step: 'Planned', calls: [planned], openCall: null, minutesWaited: null })} />);

    const steps = screen.getAllByRole('listitem');
    expect(steps.every((s) => s.getAttribute('data-state') === 'done')).toBe(true);
    expect(screen.queryByText(/min$/)).toBeNull();
  });

  it('warns when the target has passed and names the suggested tier, in both languages', () => {
    const state = chain({ minutesWaited: 66, targetPassed: true, suggestedNextTier: 'SeniorDoctor' });
    renderIn(<ChainStrip state={state} />);
    expect(screen.getByText('Target passed · tell the senior doctor?')).toBeInTheDocument();
  });

  it('warns in Turkish', () => {
    const state = chain({ minutesWaited: 66, targetPassed: true, suggestedNextTier: 'SeniorDoctor' });
    renderIn(<ChainStrip state={state} />, 'tr');
    expect(screen.getByText('Hedef aşıldı · kıdemli hekime bildir?')).toBeInTheDocument();
  });

  it('suggests critical care when the score is high, before any target passes', () => {
    renderIn(<ChainStrip state={chain({ suggestedNextTier: 'CriticalCare' })} />);
    expect(screen.getByText('Call critical care?')).toBeInTheDocument();
  });

  it('shows only the waited minutes when a call has no target', () => {
    renderIn(<ChainStrip state={chain({ openCall: call({ targetAt: null }), calls: [call({ targetAt: null })] })} />);
    expect(screen.getByText('11 min')).toBeInTheDocument();
  });
});
