import { useId, type ReactNode } from 'react';
import type { BedSummary, News2Parameter } from '../api/types';
import { subScoreKey } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import type { Messages } from '../i18n/en';
import { floorTierOf, parameterLabel, readingOf, riskText, shapeOf, tierOf } from '../risk';
import { clock, minutesBetween } from '../time';
import { Icon, Shape, type IconName } from '../ui/Icon';
import { ChainStrip } from './ChainStrip';

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

/** "Resp 22 +2": the label in the body face, the reading and points as figures. */
function Driver({ bed, parameter, t }: { bed: BedSummary; parameter: News2Parameter; t: Messages }) {
  const latest = bed.latest!;
  const reading = readingOf(parameter, latest.input, t);
  const points = latest.subScores[subScoreKey(parameter)] ?? 0;
  return (
    <span className="driver">
      {parameterLabel(parameter, t)}
      {reading && (
        <>
          {' '}
          {/* Air or oxygen is a word, not a figure: it stays in the body face. */}
          <span className={parameter === 'SupplementalOxygen' ? undefined : 'fig'}>{reading}</span>
        </>
      )}{' '}
      <span className="pts">+{points}</span>
    </span>
  );
}

/** The 4B nurse card: why the score is what it is, whether observations are late, and who has been called. */
export function BedCard({ bed, selected, onSelect }: BedCardProps) {
  const { t } = useT();
  const ids = { title: useId(), score: useId(), risk: useId(), due: useId(), details: useId() };
  const { latest, escalation } = bed;
  const tier = tierOf(latest) ?? floorTierOf(latest);
  const trend = trendText(bed, t);
  const label = bed.bed ? t.bed(bed.bed) : bed.patientId;
  const title = `${label} · ${bed.name}${bed.age !== null ? `, ${t.age(bed.age)}` : ''}`;

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
        {bed.diagnosis && (
          <span className="bed-dx" title={bed.diagnosis}>
            {t.scenarioDiagnosis(bed.diagnosis)}
          </span>
        )}
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
            {trend && <span className="trend">{trend}</span>}
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
            {latest.parametersScoring3.length > 0 && <span className="visually-hidden">{t.raisedBy}: </span>}
            {latest.parametersScoring3.map((p) => (
              <Driver key={`red-${p}`} bed={bed} parameter={p} t={t} />
            ))}
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
                  <Driver key={p} bed={bed} parameter={p} t={t} />
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
          {escalation.patientConcernAt && (
            <Chip icon="flag" tone="warn">
              {t.patientConcern(clock(escalation.patientConcernAt))}
            </Chip>
          )}
          {latest?.input.spO2Scale === 'Scale2' && <Chip icon="lungs">{t.scale2}</Chip>}
        </span>
        <ChainStrip state={escalation} />
      </span>

      <span className="bed-due" id={ids.due}>
        {latest && overdue && (
          <span className="due" data-state="overdue">
            <Icon name="clock" />
            <span>{t.overdue(bed.overdueMinutes!)}</span>
          </span>
        )}
        {latest && !overdue && bed.nextDue && (
          <span className="due">
            <Icon name="clock" />
            <span>{t.nextObs(clock(bed.nextDue))}</span>
          </span>
        )}
        {bed.notDone && (
          <span className="due" data-state="not-done" data-urgent={bed.notDone.urgent || undefined}>
            {t.notDoneCard(clock(bed.notDone.at), t[`reason${bed.notDone.reason}`])}
          </span>
        )}
        {latest?.response === 'EmergencyResponse' && <span className="due continuous">{t.continuous}</span>}
        {latest && <span className="last">{t.lastObs(clock(latest.at))}</span>}
      </span>
    </button>
  );
}
