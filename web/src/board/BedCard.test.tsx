import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BedCard } from './BedCard';
import { bed, chain, point } from '../fixtures';
import { renderIn } from '../test/render';
import type { BedSummary } from '../api/types';

function card(summary: BedSummary, lang: 'en' | 'tr' = 'en', onSelect = vi.fn()) {
  renderIn(<BedCard bed={summary} selected={false} onSelect={onSelect} />, lang);
  return screen.getByRole('button');
}

describe('BedCard risk', () => {
  const cases = [
    { name: 'score 0', latest: point({ total: 0, risk: 'Low', response: 'RoutineMonitoring' }), shape: 'circle', en: 'Low', tr: 'Düşük' },
    { name: 'score 1–4', latest: point({ total: 3, risk: 'Low', response: 'WardBasedResponse' }), shape: 'triangle', en: 'Low', tr: 'Düşük' },
    { name: 'single 3', latest: point({ total: 3, risk: 'LowMedium', response: 'UrgentWardBasedResponse' }), shape: 'diamond', en: 'Low–medium', tr: 'Düşük–orta' },
    { name: 'score 5–6', latest: point({ total: 6, risk: 'Medium' }), shape: 'diamond', en: 'Medium', tr: 'Orta' },
    { name: 'score 7+', latest: point({ total: 9, risk: 'High', response: 'EmergencyResponse' }), shape: 'square', en: 'High', tr: 'Yüksek' },
  ] as const;

  it.each(cases)('$name shows its shape and risk text in English', ({ latest, shape, en }) => {
    const button = card(bed({ latest, previous: null }));
    expect(button.querySelector(`[data-shape="${shape}"]`)).not.toBeNull();
    expect(screen.getByText(en)).toBeInTheDocument();
  });

  it.each(cases)('$name shows its risk text in Turkish', ({ latest, tr }) => {
    card(bed({ latest, previous: null }), 'tr');
    expect(screen.getByText(tr)).toBeInTheDocument();
  });
});

describe('BedCard content', () => {
  it('names the bed, patient, age and the diagnosis as served', () => {
    card(bed());
    expect(screen.getByText('Bed 02 · Ayşe K., 71')).toBeInTheDocument();
    expect(screen.getByText('Community-acquired pneumonia')).toHaveAttribute('title', 'Community-acquired pneumonia');
  });

  it('uses the Turkish bed label', () => {
    card(bed(), 'tr');
    expect(screen.getByText('Yatak 02 · Ayşe K., 71')).toBeInTheDocument();
  });

  it('shows the trend since the previous observation', () => {
    card(bed({ latest: point({ total: 8, risk: 'High' }), previous: point({ at: '2026-11-23T21:15:00+03:00', total: 6 }) }));
    expect(screen.getByText('↑ +2 / 2 h')).toBeInTheDocument();
  });

  it('shows no change when the score is the same', () => {
    card(bed({ previous: point({ at: '2026-11-23T22:15:00+03:00', total: 6 }) }));
    expect(screen.getByText('→ no change')).toBeInTheDocument();
  });

  it('shows no trend without a previous observation', () => {
    card(bed({ previous: null }));
    expect(screen.queryByText(/[↑↓→]/)).toBeNull();
  });

  it('lists at most three raising parameters with their readings', () => {
    card(bed());
    const drivers = [...document.querySelectorAll('.driver')].map((d) => d.textContent);
    expect(drivers).toEqual(['Resp 22 +2', 'SpO₂ 95% +1', 'Systolic 109 +1']);
  });

  it('says all parameters are normal when nothing raises the score', () => {
    card(bed({ drivers: [], latest: point({ total: 0, risk: 'Low', response: 'RoutineMonitoring' }) }));
    expect(screen.getByText('All parameters normal')).toBeInTheDocument();
  });
});

describe('BedCard observations due', () => {
  it('marks an overdue set strongly', () => {
    card(bed({ overdueMinutes: 25 }));
    const chip = screen.getByText('Overdue 25 min');
    expect(chip.closest('[data-state="overdue"]')).not.toBeNull();
  });

  it('shows the next set when not overdue', () => {
    card(bed({ overdueMinutes: 0 }));
    expect(screen.getByText('Next obs 00:15')).toBeInTheDocument();
  });

  it('says continuous monitoring on an emergency response', () => {
    card(bed({ latest: point({ total: 9, risk: 'High', response: 'EmergencyResponse' }) }));
    expect(screen.getByText('Continuous monitoring')).toBeInTheDocument();
  });

  it('shows an incomplete score with what is missing and the minimum', () => {
    card(
      bed({
        latest: point({
          total: null,
          minimumTotal: 4,
          risk: null,
          response: null,
          problems: [{ parameter: 'SpO2', code: 'Missing', message: 'No SpO2 reading' }],
        }),
        previous: null,
        drivers: [],
      }),
    );
    expect(screen.getByText('—')).toBeInTheDocument();
    expect(screen.getByText('SpO₂ missing')).toBeInTheDocument();
    expect(screen.getByText('at least 4')).toBeInTheDocument();
    expect(screen.getByText('Incomplete score')).toBeInTheDocument();
  });

  it('says there are no observations yet before the first set', () => {
    card(bed({ latest: null, previous: null, drivers: [], nextDue: null, overdueMinutes: null }));
    expect(screen.getByText('No observations yet')).toBeInTheDocument();
    expect(screen.queryByText(/Next obs/)).toBeNull();
  });
});

describe('BedCard escalation', () => {
  it('shows the concern time and the chain strip', () => {
    card(bed());
    expect(screen.getByText('Concern 20:15')).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Escalation' })).toBeInTheDocument();
    expect(screen.getByText('11 / 60 min')).toBeInTheDocument();
  });

  it('shows a patient or family concern', () => {
    card(bed({ escalation: chain({ patientConcernAt: '2026-11-23T21:00:00+03:00' }) }));
    expect(screen.getByText('Patient/family concern 21:00')).toBeInTheDocument();
  });

  it('no longer shows the old doctor chips', () => {
    card(bed({ escalation: chain({ step: 'Assessed', openCall: null, minutesWaited: null }) }));
    expect(screen.queryByText(/Doctor called .* waiting/)).toBeNull();
    expect(screen.queryByText(/Doctor arrived/)).toBeNull();
  });

  it('shows no note about missing events on the card (the chain comes from FHIR for every source)', () => {
    card(bed({ eventsAvailable: false }));
    expect(screen.queryByText(/not available from this data source/)).toBeNull();
  });

  it('marks Scale 2', () => {
    card(bed({ latest: point({ input: { ...point().input, spO2Scale: 'Scale2' } }) }));
    expect(screen.getByText('SpO₂ Scale 2')).toBeInTheDocument();
  });
});

describe('BedCard as a control', () => {
  it('is a pressed-state button named by bed, risk and overdue state', async () => {
    const onSelect = vi.fn();
    const button = card(bed({ overdueMinutes: 25 }), 'en', onSelect);

    expect(button).toHaveAttribute('aria-pressed', 'false');
    expect(button).toHaveAccessibleName(/Bed 02/);
    expect(button).toHaveAccessibleName(/Medium/);
    expect(button).toHaveAccessibleName(/Overdue 25 min/);

    await userEvent.click(button);
    expect(onSelect).toHaveBeenCalledWith('synthetic-02');
  });
});
