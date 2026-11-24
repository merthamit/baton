import type { News2Parameter, ScorePoint } from '../api/types';
import { subScoreKey } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { parameterLabel, readingOf } from '../risk';
import { clock } from '../time';

const columns: News2Parameter[] = [
  'RespirationRate',
  'SpO2',
  'SupplementalOxygen',
  'SystolicBp',
  'PulseRate',
  'Temperature',
  'Consciousness',
];

/** Every scored observation set. A cell's sub-score is written next to the reading ("22 +3") and also shaded. */
export function ObservationTable({ points, scale2 }: { points: ScorePoint[]; scale2: boolean }) {
  const { t } = useT();

  return (
    <div className="tablewrap">
      <table>
        <caption className="visually-hidden">{t.observations}</caption>
        <thead>
          <tr>
            <th scope="col">{t.colTime}</th>
            {columns.map((c) => (
              <th key={c} scope="col">
                {c === 'SpO2' && scale2 ? `${parameterLabel(c, t)} (${t.scale2Short})` : parameterLabel(c, t)}
              </th>
            ))}
            <th scope="col">{t.colTotal}</th>
          </tr>
        </thead>
        <tbody>
          {points.map((p) => (
            <tr key={p.at}>
              <th scope="row" className="mono">
                {clock(p.at)}
              </th>
              {columns.map((c) => {
                const sub = p.subScores[subScoreKey(c)];
                return (
                  <td key={c} data-sub={sub ?? 'none'}>
                    <span>{readingOf(c, p.input, t) ?? '—'}</span>
                    {sub !== null && sub > 0 && <sup className="sub">+{sub}</sup>}
                  </td>
                );
              })}
              <td className="total">{p.total ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
