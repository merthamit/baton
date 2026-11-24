import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { useLiveChanges, type WardChange } from './useLiveChanges';
import { FakeEventSource } from '../test/fakeEventSource';

function Probe({ onChange, trySession }: { onChange: (c: WardChange) => void; trySession?: string }) {
  const status = useLiveChanges(onChange, trySession);
  return <p>{status}</p>;
}

const change = (overrides: Partial<WardChange> = {}): WardChange => ({
  kind: 'Fhir',
  patientId: 'synthetic-02',
  trySession: null,
  at: '2026-11-24T08:00:00Z',
  ...overrides,
});

describe('useLiveChanges', () => {
  beforeEach(() => FakeEventSource.install());

  it('opens /api/live and reports the link status', () => {
    render(<Probe onChange={vi.fn()} />);
    const source = FakeEventSource.last();

    expect(source.url).toBe('/api/live');
    expect(screen.getByText('connecting')).toBeInTheDocument();
    act(() => source.emit('ready', {}));
    expect(screen.getByText('open')).toBeInTheDocument();
    act(() => source.onerror?.());
    expect(screen.getByText('reconnecting')).toBeInTheDocument();
  });

  it('says when the link is closed for good', () => {
    render(<Probe onChange={vi.fn()} />);
    const source = FakeEventSource.last();

    source.readyState = 2;
    act(() => source.onerror?.());

    expect(screen.getByText('closed')).toBeInTheDocument();
  });

  it('passes on a change', () => {
    const onChange = vi.fn();
    render(<Probe onChange={onChange} />);

    act(() => FakeEventSource.last().emit('change', change()));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ kind: 'Fhir', patientId: 'synthetic-02' }));
  });

  it("ignores another Try session's change", () => {
    const onChange = vi.fn();
    render(<Probe onChange={onChange} trySession="mine" />);

    act(() => FakeEventSource.last().emit('change', change({ kind: 'Try', trySession: 'theirs' })));
    expect(onChange).not.toHaveBeenCalled();

    act(() => FakeEventSource.last().emit('change', change({ kind: 'Try', trySession: 'mine' })));
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it("asks for its own Try session's changes", () => {
    render(<Probe onChange={vi.fn()} trySession="abc" />);

    expect(FakeEventSource.last().url).toBe('/api/live?try=abc');
  });

  it('closes the stream on unmount', () => {
    const { unmount } = render(<Probe onChange={vi.fn()} />);
    unmount();
    expect(FakeEventSource.last().closed).toBe(true);
  });
});
