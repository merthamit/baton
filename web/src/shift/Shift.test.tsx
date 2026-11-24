import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderIn } from '../test/render';
import { bed, noChain, point } from '../fixtures';
import type { DoctorCall, News2Preview, ShiftState } from '../api/types';
import { ObservationForm } from './ObservationForm';
import { DoctorView } from './DoctorView';
import { ShiftBar, wardNow } from './ShiftBar';
import { BedCard } from '../board/BedCard';
import { ChainStrip } from '../board/ChainStrip';
import { readUrlState, writeUrlState } from '../url';

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

type Handler = (url: string, init?: RequestInit) => Response;

function route(routes: Record<string, Handler>) {
  const fetchMock = vi.fn((url: string, init?: RequestInit) => {
    const key = Object.keys(routes).find((k) => url.startsWith(k));
    return Promise.resolve(key ? routes[key]!(url, init) : respond(404, { title: 'Not found' }));
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

const shiftId = 'a'.repeat(32);
const measured = {
  at: '2026-11-23T22:10:00+03:00',
  vitals: { respirationRate: 24, spO2: 93, oxygenLitresPerMinute: 0, systolicBp: 100, pulseRate: 112, temperatureCelsius: 38.6, consciousness: 'Alert' },
  scale: 'Scale1',
};
const preview: News2Preview = {
  isComplete: true,
  total: 7,
  risk: 'High',
  response: 'EmergencyResponse',
  subScores: { respirationRate: 2, spO2: 2, supplementalOxygen: 0, systolicBp: 2, pulseRate: 1, consciousness: 0, temperature: 1 },
  parametersScoring3: [],
};

describe('bedside entry form', () => {
  it('measures, previews the score from the engine, saves and offers the call', async () => {
    const calls: string[] = [];
    route({
      [`/api/shift/${shiftId}/patients/synthetic-02/measure`]: () => respond(200, measured),
      '/api/news2': () => respond(200, preview),
      [`/api/shift/${shiftId}/observations`]: (_, init) => {
        calls.push(String(init?.body));
        return respond(200, { latest: point({ at: measured.at, total: 7, risk: 'High', response: 'EmergencyResponse' }), nextDue: '2026-11-23T22:40:00+03:00', escalation: noChain });
      },
      [`/api/shift/${shiftId}/actions`]: (_, init) => {
        calls.push(String(init?.body));
        return respond(200, {});
      },
    });
    const onSaved = vi.fn();
    renderIn(<ObservationForm shiftId={shiftId} patientId="synthetic-02" bedLabel="Bed 02" name="Ayşe K." onSaved={onSaved} onClose={() => {}} />);

    await userEvent.click(screen.getByRole('button', { name: 'Measure now' }));
    expect(screen.getByLabelText('Respiration rate (/min)')).toHaveValue("24");
    expect(await screen.findByText('Emergency assessment within 30 minutes by the critical care team; continuous monitoring.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Next set due 22:40')).toBeInTheDocument();
    expect(JSON.parse(calls[0]!)).toMatchObject({ patientId: 'synthetic-02', vitals: { respirationRate: 24, consciousness: 'Alert' } });
    expect(onSaved).toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Call Critical care' }));
    await waitFor(() => expect(calls).toHaveLength(2));
    expect(JSON.parse(calls[1]!)).toMatchObject({ kind: 'Call', tier: 'CriticalCare' });
  });

  it('names the refused field in the interface language', async () => {
    route({
      '/api/news2': () => respond(200, preview),
      [`/api/shift/${shiftId}/observations`]: () =>
        respond(400, { title: 'Entry not allowed', type: 'urn:baton:shift:out-of-range', detail: 'spO2 must be from 50 to 100.', field: 'spO2' }),
    });
    renderIn(<ObservationForm shiftId={shiftId} patientId="synthetic-02" bedLabel="Yatak 02" name="Ayşe K." onSaved={() => {}} onClose={() => {}} />, 'tr');

    for (const [label, value] of [
      ['Solunum sayısı (/dk)', '24'],
      ['SpO₂ (%)', '130'],
      ['Sistolik TA (mmHg)', '100'],
      ['Nabız (/dk)', '112'],
      ['Ateş (°C)', '38.6'],
    ] as const) {
      await userEvent.type(screen.getByLabelText(label), value);
    }
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('“SpO₂ (%)” alanını kontrol edin');
  });

  it('keeps Save off until every field is a number', () => {
    route({});
    renderIn(<ObservationForm shiftId={shiftId} patientId="synthetic-02" bedLabel="Bed 02" name="Ayşe K." onSaved={() => {}} onClose={() => {}} />);

    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    expect(screen.getByText('Fill in every field to see the score.')).toBeInTheDocument();
  });
});

function doctorCall(over: Partial<DoctorCall> = {}): DoctorCall {
  return {
    patientId: 'synthetic-03',
    bed: '03',
    name: 'Mehmet D.',
    latest: point({ total: 6, risk: 'Medium' }),
    step: 'Called',
    call: {
      callId: 'c1',
      tier: 'OnCallDoctor',
      calledAt: '2026-11-23T22:05:00+03:00',
      targetAt: '2026-11-23T23:05:00+03:00',
      arrivedAt: null,
      arrivalSimulated: false,
      assessedAt: null,
      assessment: null,
      plannedAt: null,
      plan: null,
      acknowledgedAt: null,
    },
    minutesSinceCall: 5,
    targetPassed: false,
    suggestedNextTier: null,
    ...over,
  };
}

describe('doctor screen', () => {
  it('offers acknowledge and arrive on a new call, and sends the step', async () => {
    const bodies: string[] = [];
    route({ [`/api/shift/${shiftId}/actions`]: (_, init) => (bodies.push(String(init?.body)), respond(200, {})) });
    const onChanged = vi.fn();
    renderIn(<DoctorView shiftId={shiftId} calls={[doctorCall()]} onChanged={onChanged} />);

    expect(screen.getByText('On-call doctor called 22:05 · 5 min ago')).toBeInTheDocument();
    expect(screen.getByText('Be there by 23:05')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Acknowledge — coming' }));

    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    expect(JSON.parse(bodies[0]!)).toMatchObject({ patientId: 'synthetic-03', kind: 'Acknowledge' });
    expect(screen.getByRole('button', { name: 'I have arrived' })).toBeInTheDocument();
  });

  it('asks for the assessment once arrived, and shows a passed target', () => {
    route({});
    renderIn(
      <DoctorView
        shiftId={shiftId}
        calls={[doctorCall({ step: 'Arrived', call: { ...doctorCall().call, arrivedAt: '2026-11-23T22:30:00+03:00' } }), doctorCall({ patientId: 'synthetic-04', bed: '04', targetPassed: true, call: { ...doctorCall().call, callId: 'c2' } })]}
        onChanged={() => {}}
      />,
    );

    expect(screen.getByRole('button', { name: 'Record assessment' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Record plan' })).not.toBeInTheDocument();
    expect(screen.getByText('Target passed')).toBeInTheDocument();
  });

  it('says when nothing is waiting', () => {
    route({});
    renderIn(<DoctorView shiftId={shiftId} calls={[]} onChanged={() => {}} />);

    expect(screen.getByText('No calls waiting. When the nurse calls, the call appears here.')).toBeInTheDocument();
  });
});

describe('shift clock and link', () => {
  const state: ShiftState = { shiftId, startedAt: '2026-11-23T22:00:00+03:00', now: '2026-11-23T22:00:00+03:00', speed: 10, shiftEnd: '2026-11-24T08:00:00+03:00' };

  it('runs the ward clock at the shift speed between answers, and stops at the end', () => {
    expect(wardNow(state, 1000, 61_000)).toBe(Date.parse('2026-11-23T22:10:00+03:00'));
    expect(wardNow(state, 1000, 1000 + 24 * 3_600_000)).toBe(Date.parse(state.shiftEnd));
  });

  it('keeps the shift and the doctor role in the link', () => {
    const search = writeUrlState({ shift: shiftId, role: 'doctor' });
    expect(search).toBe(`?shift=${shiftId}&role=doctor`);
    expect(readUrlState(search)).toEqual({ shift: shiftId, role: 'doctor' });
  });

  it('ignores a role without a shift and a malformed shift id', () => {
    expect(readUrlState('?role=doctor')).toEqual({});
    expect(readUrlState('?shift=../x&role=doctor')).toEqual({});
  });
});

describe('review fixes', () => {
  it('previews with the patient and the shift, and reads a decimal comma', async () => {
    const bodies: string[] = [];
    route({
      '/api/news2': (_, init) => {
        bodies.push(String(init?.body));
        return respond(200, preview);
      },
    });
    renderIn(<ObservationForm shiftId={shiftId} patientId="synthetic-04" bedLabel="Bed 04" name="Ali V." onSaved={() => {}} onClose={() => {}} />);

    for (const [label, value] of [
      ['Respiration rate (/min)', '20'],
      ['SpO₂ (%)', '89'],
      ['Systolic BP (mmHg)', '120'],
      ['Pulse (/min)', '90'],
      ['Temperature (°C)', '37,5'],
    ] as const) {
      await userEvent.type(screen.getByLabelText(label), value);
    }

    await waitFor(() => expect(bodies.length).toBeGreaterThan(0));
    expect(JSON.parse(bodies.at(-1)!)).toMatchObject({ patientId: 'synthetic-04', shift: shiftId, vitals: { temperatureCelsius: 37.5 } });
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
  });

  it('shows a not-done reason instead of a plain overdue', () => {
    renderIn(
      <BedCard
        bed={bed({ overdueMinutes: 25, notDone: { at: '2026-11-23T22:10:00+03:00', reason: 'CouldNotBeObtained', note: null, urgent: true } })}
        selected={false}
        onSelect={() => {}}
      />,
    );

    expect(screen.getByText('Not done 22:10: No reading could be obtained — escalate now')).toBeInTheDocument();
    expect(screen.queryByText('Overdue 25 min')).not.toBeInTheDocument();
  });

  it('shows the acknowledge step as pending on a shift call', () => {
    const call = { ...doctorCall().call, callId: 'shift-abc-synthetic-02-1' };
    renderIn(<ChainStrip state={{ ...noChain, step: 'Called', calls: [call], openCall: call, minutesWaited: 3 }} />);

    const step = screen.getByText('Acknowledged').closest('li')!;
    expect(step).toHaveAttribute('data-state', 'todo');
    expect(step).toHaveTextContent('(not yet)');
  });

  it('asks before ending the shift for every tab', async () => {
    const onEnd = vi.fn();
    const state: ShiftState = { shiftId, startedAt: '2026-11-23T22:00:00+03:00', now: '2026-11-23T22:00:00+03:00', speed: 10, shiftEnd: '2026-11-24T08:00:00+03:00' };
    renderIn(<ShiftBar state={state} fetchedAt={Date.now()} role="nurse" otherRoleHref="/" onRole={() => {}} onSpeed={() => {}} onEnd={onEnd} />);

    await userEvent.click(screen.getByRole('button', { name: 'End shift' }));
    expect(onEnd).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));
    expect(onEnd).toHaveBeenCalled();
  });
});
