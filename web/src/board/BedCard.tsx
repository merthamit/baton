import { useId, type ReactNode } from 'react';
import type { BedSummary, News2Parameter } from '../api/types';
import { subScoreKey } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import type { Messages } from '../i18n/en';
import { parameterLabel, readingOf, riskText, shapeOf, tierOf } from '../risk';
import { clock, minutesBetween } from '../time';
import { Icon, Shape, type IconName } from '../ui/Icon';

interface BedCardProps {
  bed: BedSummary;
  selected: boolean;
  onSelect: (patientId: string) => void;
}

function Chip({ icon, tone, children }: { icon?: IconName; tone?: 'warn' | 'bad'; children: ReactNode }) {
  return (
    <span className="chip" data-tone={tone}>
      {icon && <Icon name={icon} />}
      <span>{children}</span>
    </span>
  );
}

function trendText(bed: BedSummary, t: Messages): string | null {
  const { latest, previous } = bed;
  if (!latest || !previous || latest.total === null || previous.total === null) {
    return null;
  }
  const change = latest.total - previous.total;
  const hours = String(Number((minutesBetween(previous.at, latest.at) / 60).toFixed(1)));
  return change > 0 ? t.trendUp(change, hours) : change < 0 ? t.trendDown(change, hours) : t.trendFlat;
}

function driverText(bed: BedSummary, parameter: News2Parameter, t: Messages): string {
  const latest = bed.latest!;
  const reading = readingOf(parameter, latest.input, t);
  const points = latest.subScores[subScoreKey(parameter)] ?? 0;
  return `${parameterLabel(parameter, t)}${reading ? ` ${reading}` : ''} +${points}`;
}

/** The 4B nurse card: why the score is what it is, whether observations are late, and who has been called. */
export function BedCard({ bed, selected, onSelect }: BedCardProps) {
  const { t } = useT();
  const ids = { title: useId(), score: useId(), risk: useId(), due: useId(), details: useId() };
  const { latest, escalation } = bed;
  const tier = tierOf(latest);
  const trend = trendText(bed, t);
  const label = bed.bed ? t.bed(bed.bed) : bed.patientId;
  const title = `${label} · ${bed.name}${bed.age !== null ? `, ${t.age(bed.age)}` : ''}`;

  const waiting =
    escalation.doctorCalledAt && escalation.waitingMinutes !== null
      ? t.doctorCalledWaiting(clock(escalation.doctorCalledAt), escalation.waitingMinutes)
      : null;
  const overdue = bed.overdueMinutes !== null && bed.overdueMinutes > 0;

  return (
    <button
      type="button"
      className="bed"
      data-tier={tier ?? 'none'}
      data-overdue={overdue || undefined}
      aria-pressed={selected}
      aria-labelledby={`${ids.title} ${ids.score} ${ids.risk} ${ids.due}`}
      aria-describedby={ids.details}
      onClick={() => onSelect(bed.patientId)}
    >
      <span className="bed-head">
        <span className="bed-title" id={ids.title}>
          {title}
        </span>
        {bed.diagnosis && <span className="bed-dx">{t.scenarioDiagnosis(bed.diagnosis)}</span>}
      </span>

      {latest ? (
        <span className="bed-score">
          <span className="score" id={ids.score}>
            <Shape shape={shapeOf(tier)} />
            <span className="score-n mono">{latest.total ?? '—'}</span>
          </span>
          <span className="score-side">
            <span className="risk" id={ids.risk}>
              {riskText(latest, t)}
            </span>
            {trend && <span className="trend mono">{trend}</span>}
          </span>
        </span>
      ) : (
        <span className="bed-empty" id={ids.score}>
          {t.noObservations}
        </span>
      )}

      <span className="bed-details" id={ids.details}>
        {latest && latest.total === null && (
          <span className="drivers">
            {latest.problems.map((p) => (
              <span key={p.parameter}>
                {p.code === 'Missing' ? t.missing(parameterLabel(p.parameter, t)) : t.notScored(parameterLabel(p.parameter, t))}
              </span>
            ))}
            {latest.minimumTotal !== null && <span>{t.atLeast(latest.minimumTotal)}</span>}
          </span>
        )}
        {latest && latest.total !== null && (
          <span className="drivers">
            {bed.drivers.length > 0 ? (
              <>
                <span className="visually-hidden">{t.raisedBy}: </span>
                {bed.drivers.slice(0, 3).map((p) => (
                  <span key={p} className="driver mono">
                    {driverText(bed, p, t)}
                  </span>
                ))}
              </>
            ) : (
              <span className="muted">{t.allNormal}</span>
            )}
          </span>
        )}

        <span className="chips">
          {escalation.concernRaisedAt && (
            <Chip icon="flag" tone="warn">
              {t.concern(clock(escalation.concernRaisedAt))}
            </Chip>
          )}
          {waiting && (
            <Chip icon="phone" tone="bad">
              {waiting}
            </Chip>
          )}
          {!waiting && escalation.doctorArrivedAt && <Chip icon="doctor">{t.doctorArrived(clock(escalation.doctorArrivedAt))}</Chip>}
          {escalation.criticalCareCalledAt && (
            <Chip icon="critical" tone="bad">
              {t.criticalCare(clock(escalation.criticalCareCalledAt))}
            </Chip>
          )}
          {latest?.input.spO2Scale === 'Scale2' && <Chip icon="lungs">{t.scale2}</Chip>}
          {!bed.eventsAvailable && <span className="note">{t.eventsUnavailable}</span>}
        </span>
      </span>

      <span className="bed-due" id={ids.due}>
        {latest && overdue && (
          <span className="due" data-state="overdue">
            <Icon name="clock" />
            <span>{t.overdue(bed.overdueMinutes!)}</span>
          </span>
        )}
        {latest && !overdue && bed.nextDue && (
          <span className="due mono">
            <Icon name="clock" />
            <span>{t.nextObs(clock(bed.nextDue))}</span>
          </span>
        )}
        {latest?.response === 'EmergencyResponse' && <span className="due continuous">{t.continuous}</span>}
        {latest && <span className="last mono">{t.lastObs(clock(latest.at))}</span>}
      </span>
    </button>
  );
}
