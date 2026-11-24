import { describe, expect, it } from 'vitest';
import { readUrlState, writeUrlState } from './url';

describe('URL state', () => {
  it('round-trips the time and the bed', () => {
    const search = writeUrlState({ at: '2026-11-23T23:30:00+03:00', bed: 'synthetic-02' });

    expect(search).toBe('?at=2026-11-23T23%3A30%3A00%2B03%3A00&bed=synthetic-02');
    expect(readUrlState(search)).toEqual({ at: '2026-11-23T23:30:00+03:00', bed: 'synthetic-02' });
  });

  it('leaves out what is not set', () => {
    expect(writeUrlState({})).toBe('');
    expect(readUrlState('')).toEqual({});
  });

  it('round-trips view=handover in the URL', () => {
    const search = writeUrlState({ bed: 'synthetic-02', view: 'handover' });

    expect(search).toBe('?bed=synthetic-02&view=handover');
    expect(readUrlState(search)).toEqual({ bed: 'synthetic-02', view: 'handover' });
    expect(readUrlState('?view=other')).toEqual({});
  });

  it('ignores a time without an offset', () => {
    expect(readUrlState('?at=2026-11-23T23:30:00')).toEqual({});
  });
});
