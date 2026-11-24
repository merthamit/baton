import type { BedSummary } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { floorTierOf, tierOf } from '../risk';
import { Icon, Shape } from '../ui/Icon';

export interface WardCounts {
  high: number;
  medium: number;
  overdue: number;
  waiting: number;
  concerns: number;
}

/** What the nurse in charge reads first: how many beds need someone now. Counts only; beds keep their places. */
export function countWard(beds: BedSummary[]): WardCounts {
  const tier = (b: BedSummary) => tierOf(b.latest) ?? floorTierOf(b.latest);
  return {
    high: beds.filter((b) => tier(b) === 3).length,
    medium: beds.filter((b) => tier(b) === 2).length,
    overdue: beds.filter((b) => (b.overdueMinutes ?? 0) > 0 || b.notDone?.urgent).length,
    waiting: beds.filter((b) => b.escalation.openCall !== null).length,
    concerns: beds.filter((b) => b.escalation.concernRaisedAt !== null || b.escalation.patientConcernAt !== null).length,
  };
}

/** The ward at a glance: high and medium risk, sets overdue, calls waiting, concerns raised. */
export function WardSummary({ beds }: { beds: BedSummary[] }) {
  const { t } = useT();
  const c = countWard(beds);
  const items = [
    { key: 'high', n: c.high, label: t.sumHigh, tone: 'bad', mark: <Shape shape="square" /> },
    { key: 'medium', n: c.medium, label: t.sumMedium, tone: 'warn', mark: <Shape shape="diamond" /> },
    { key: 'overdue', n: c.overdue, label: t.sumOverdue, tone: 'bad', mark: <Icon name="clock" /> },
    { key: 'waiting', n: c.waiting, label: t.sumWaiting, tone: 'warn', mark: <Icon name="doctor" /> },
    { key: 'concerns', n: c.concerns, label: t.sumConcerns, tone: 'warn', mark: <Icon name="flag" /> },
  ];

  return (
    <section className="summary" aria-label={t.sumLabel}>
      <p className="summary-total">{t.sumBeds(beds.length)}</p>
      <ul>
        {items.map((i) => (
          <li key={i.key} data-tone={i.n > 0 ? i.tone : 'quiet'}>
            <span className="summary-mark" aria-hidden="true">
              {i.mark}
            </span>
            <span className="summary-n mono">{i.n}</span>
            <span className="summary-label">{i.label}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
