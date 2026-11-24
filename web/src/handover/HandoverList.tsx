import type { HandoverResponse } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { riskName, shapeOf, tierOfRisk } from '../risk';
import { clock } from '../time';
import { Icon, Shape } from '../ui/Icon';
import './handover.css';

interface HandoverListProps {
  response: HandoverResponse;
  selectedId: string | undefined;
  onSelect: (patientId: string) => void;
}

/**
 * The ward's handover, in the API's order (sickest first, missing items first within a risk). Each row: bed and name,
 * the current score with its shape and risk word, a one-line state, what the record is missing, and whether the
 * patient has been taken over.
 */
export function HandoverList({ response, selectedId, onSelect }: HandoverListProps) {
  const { t } = useT();

  return (
    <section className="handover" aria-labelledby="handover-title">
      <div className="handover-head">
        <h2 id="handover-title">{t.handoverTitle}</h2>
        <p className="muted">{t.handoverWindow(clock(response.windowStart), clock(response.at))}</p>
        <p className="handover-count">{t.handoverCount(response.confirmedCount, response.rows.length)}</p>
      </div>
      <ul className="handover-list">
        {response.rows.map((row) => {
          const tier = tierOfRisk(row.sbar.currentRisk, row.sbar.currentTotal);
          const bedLabel = row.bed ? t.bed(row.bed) : row.patientId;
          return (
            <li key={row.patientId}>
              <button
                type="button"
                className="handover-row"
                data-tier={tier ?? undefined}
                aria-pressed={row.patientId === selectedId}
                onClick={() => onSelect(row.patientId)}
              >
                <span className="hr-who">
                  {bedLabel} · {row.name}
                </span>
                <span className="hr-score">
                  <span className="score">
                    <Shape shape={shapeOf(tier)} />
                    <span className="score-n mono">{row.sbar.currentTotal ?? '—'}</span>
                  </span>
                  <span className="risk">{riskName(row.sbar.currentRisk, t)}</span>
                </span>
                <span className="hr-state">{row.sbar.stateLine}</span>
                {row.sbar.missing.length > 0 && (
                  <span className="chips hr-missing">
                    {row.sbar.missing.map((m) => (
                      <span key={m.kind + m.text} className="chip" data-tone="bad">
                        <Icon name="note" />
                        {m.text}
                      </span>
                    ))}
                  </span>
                )}
                <span className="hr-status">
                  {row.confirmed ? (
                    <>
                      {t.takenOverAt(clock(row.confirmed.at))}
                      {row.newDataSinceHandover && <span className="hr-new"> · {t.newDataSince}</span>}
                    </>
                  ) : (
                    t.openSbar
                  )}
                </span>
              </button>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
