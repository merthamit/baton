import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { bed, noChain, point } from '../fixtures';
import { renderIn } from '../test/render';
import { ThemeToggle } from '../ui/ThemeToggle';
import { countWard, WardSummary } from './WardSummary';

const call = {
  callId: 'c1',
  tier: 'OnCallDoctor' as const,
  calledAt: '2026-11-23T23:19:00+03:00',
  targetAt: null,
  arrivedAt: null,
  arrivalSimulated: false,
  assessedAt: null,
  assessment: null,
  plannedAt: null,
  plan: null,
};

const beds = [
  bed({ patientId: 'a', latest: point({ total: 8, risk: 'High' }), escalation: noChain }),
  bed({ patientId: 'b', latest: point({ total: 5, risk: 'Medium' }), overdueMinutes: 20, escalation: noChain }),
  bed({ patientId: 'c', latest: point({ total: 0, risk: 'Low' }), escalation: { ...noChain, step: 'Called', calls: [call], openCall: call, concernRaisedAt: '2026-11-23T20:15:00+03:00' } }),
  bed({ patientId: 'd', latest: point({ total: 1, risk: 'Low' }), escalation: noChain }),
];

describe('ward summary', () => {
  it('counts high and medium risk, overdue sets, calls waiting and concerns', () => {
    expect(countWard(beds)).toEqual({ high: 1, medium: 1, overdue: 1, waiting: 1, concerns: 1 });
  });

  it('reads the counts with their words, in Turkish too', () => {
    renderIn(<WardSummary beds={beds} />, 'tr');

    expect(screen.getByText('4 yatak')).toBeInTheDocument();
    expect(screen.getByText('yüksek risk').closest('li')).toHaveTextContent('1yüksek risk');
    expect(screen.getByText('yanıt bekleyen çağrı').closest('li')).toHaveAttribute('data-tone', 'warn');
  });
});

describe('theme switch', () => {
  it('is light by default and remembers dark', async () => {
    localStorage.removeItem('baton.theme');
    renderIn(<ThemeToggle />);

    expect(document.documentElement.dataset.theme).toBe('light');
    await userEvent.click(screen.getByRole('button', { name: 'Dark' }));

    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(localStorage.getItem('baton.theme')).toBe('dark');
  });
});
