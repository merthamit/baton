import { describe, expect, it } from 'vitest';
import { addMinutes, clock, minutesBetween } from './time';

// The board must show ward time (the offset in the API's timestamps), never the browser's zone.
// vite.config.ts runs the tests in America/New_York.
describe('time helpers', () => {
  it('runs in a browser zone that is not the ward’s', () => {
    expect(new Date('2026-11-23T20:15:00+03:00').getHours()).not.toBe(20);
  });

  it('adds minutes across midnight and keeps the offset', () => {
    expect(addMinutes('2026-11-23T23:30:00+03:00', 45)).toBe('2026-11-24T00:15:00+03:00');
  });

  it('subtracts minutes', () => {
    expect(addMinutes('2026-11-23T08:00:00+03:00', -90)).toBe('2026-11-23T06:30:00+03:00');
  });

  it('reads the clock in the timestamp’s own offset', () => {
    expect(clock('2026-11-24T00:15:00+03:00')).toBe('00:15');
    expect(clock('2026-11-23T20:15:00.0000000+03:00')).toBe('20:15');
  });

  it('counts minutes between instants across midnight', () => {
    expect(minutesBetween('2026-11-23T23:19:00+03:00', '2026-11-24T00:05:00+03:00')).toBe(46);
  });

  it('handles a UTC timestamp', () => {
    expect(addMinutes('2026-11-23T20:30:00Z', 30)).toBe('2026-11-23T21:00:00Z');
    expect(clock('2026-11-23T20:30:00Z')).toBe('20:30');
  });
});
