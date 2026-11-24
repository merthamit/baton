import { describe, expect, it, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import { ChainPanel } from './ChainPanel';
import { chain, noChain } from '../fixtures';
import { renderIn } from '../test/render';
import type { EscalationRecordView } from '../api/types';

const records: EscalationRecordView[] = [
  { at: '2026-11-24T00:30:00+03:00', kind: 'Arrived', by: 'Practitioner/synthetic-cct', tier: null, text: null, simulated: true },
  { at: '2026-11-23T20:15:00+03:00', kind: 'ConcernRaised', by: 'Practitioner/synthetic-nurse', tier: null, text: 'Restless, not eating', simulated: null },
  { at: '2026-11-23T23:19:00+03:00', kind: 'Called', by: 'Practitioner/synthetic-nurse', tier: 'OnCallDoctor', text: null, simulated: null },
  { at: '2026-11-23T21:00:00+03:00', kind: 'PatientOrFamilyConcern', by: 'RelatedPerson/synthetic-daughter', tier: null, text: 'She is not herself', simulated: null },
];

describe('ChainPanel', () => {
  it('lists records in time order with time, who and text', () => {
    renderIn(<ChainPanel state={chain()} records={records} />);
    const items = within(screen.getByRole('list', { name: 'Escalation chain' })).getAllByRole('listitem');

    expect(items.map((i) => i.querySelector('.tm')?.textContent)).toEqual(['20:15', '21:00', '23:19', '00:30']);
    expect(within(items[0]!).getByText('Concern flag')).toBeInTheDocument();
    expect(within(items[0]!).getByText('Restless, not eating')).toBeInTheDocument();
    expect(within(items[0]!).getByText('Ward nurse')).toBeInTheDocument();
    expect(within(items[1]!).getByText('Family')).toBeInTheDocument();
    expect(within(items[2]!).getByText('On-call doctor called')).toBeInTheDocument();
  });

  it('labels a simulated arrival', () => {
    renderIn(<ChainPanel state={chain()} records={records} />);
    const last = within(screen.getByRole('list', { name: 'Escalation chain' })).getAllByRole('listitem').at(-1)!;
    expect(within(last).getByText('simulated')).toBeInTheDocument();
    expect(within(last).getByText('Critical care team')).toBeInTheDocument();
  });

  it('names Try mode actions as yours', () => {
    const tryRecords: EscalationRecordView[] = [
      { at: '2026-11-24T01:00:00+03:00', kind: 'Called', by: 'Practitioner/synthetic-you', tier: 'OnCallDoctor', text: null, simulated: null },
    ];
    renderIn(<ChainPanel state={chain()} records={tryRecords} />);
    expect(screen.getByText('You (nurse)')).toBeInTheDocument();
  });

  it('says when there is no escalation yet', () => {
    renderIn(<ChainPanel state={noChain} records={[]} />);
    expect(screen.getByText('No escalation yet.')).toBeInTheDocument();
  });
});

describe('ChainPanel keys', () => {
  it('lists two calls in the same minute without a key clash', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const same: EscalationRecordView[] = [
      { at: '2026-11-23T23:19:00+03:00', kind: 'Called', by: 'Practitioner/synthetic-you', tier: 'OnCallDoctor', text: null, simulated: null },
      { at: '2026-11-23T23:19:00+03:00', kind: 'Called', by: 'Practitioner/synthetic-you', tier: 'SeniorDoctor', text: null, simulated: null },
    ];
    try {
      renderIn(<ChainPanel state={chain()} records={same} />);
      expect(error.mock.calls.some((c) => String(c[0]).includes('same key'))).toBe(false);
    } finally {
      error.mockRestore();
    }
  });
});
