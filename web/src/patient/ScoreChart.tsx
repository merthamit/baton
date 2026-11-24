import type { ScorePoint, WardEvent } from '../api/types';
import { tierOf } from '../risk';
import { addMinutes, clock, minutesBetween } from '../time';
import { Icon, type IconName } from '../ui/Icon';

interface ScoreChartProps {
  label: string;
  points: ScorePoint[];
  events: WardEvent[];
  at: string;
  shiftStart: string;
  shiftEnd: string;
}

const W = 720;
const H = 250;
const L = 30;
const R = 14;
const T = 34;
const B = 26;

const eventIcon: Partial<Record<WardEvent['kind'], IconName>> = {
  Concern: 'flag',
  Alert: 'bell',
  DoctorCalled: 'phone',
  DoctorArrived: 'doctor',
  TreatmentStarted: 'drop',
  CriticalCareCalled: 'critical',
};

const tierColour = ['var(--ok)', 'var(--mid)', 'var(--warn)', 'var(--bad)'];

/**
 * NEWS2 over the shift on the four NEWS2 bands, with event markers and a line at the chosen time. Hand-drawn SVG;
 * the observation table below is its full text equivalent.
 */
export function ScoreChart({ label, points, events, at, shiftStart, shiftEnd }: ScoreChartProps) {
  const span = Math.max(1, minutesBetween(shiftStart, shiftEnd));
  const top = Math.max(12, ...points.map((p) => p.total ?? 0)) + 1;
  const x = (iso: string) => L + ((W - L - R) * Math.min(span, Math.max(0, minutesBetween(shiftStart, iso)))) / span;
  const y = (v: number) => T + (H - T - B) * (1 - v / top);

  const bands: [string, number, number, string][] = [
    ['0', 0, 0.5, 'var(--band0)'],
    ['1-4', 0.5, 4.5, 'var(--band1)'],
    ['5-6', 4.5, 6.5, 'var(--band2)'],
    ['7+', 6.5, top, 'var(--band3)'],
  ];

  // Step line: a score holds until the next observation.
  const scored = points.filter((p) => p.total !== null);
  const line = scored
    .map((p, i) => {
      const px = x(p.at);
      const py = y(p.total!);
      return i === 0 ? `M${px},${py}` : `L${px},${y(scored[i - 1]!.total!)} L${px},${py}`;
    })
    .join(' ');

  const ticks = Array.from({ length: 7 }, (_, i) => Math.round((span * i) / 6));
  const markers = events.filter((e) => eventIcon[e.kind] && minutesBetween(shiftStart, e.at) >= 0);

  return (
    <svg className="chart" viewBox={`0 0 ${W} ${H}`} role="img" aria-label={label}>
      {bands.map(([name, from, to, fill]) => (
        <rect key={name} data-band={name} x={L} y={y(to)} width={W - L - R} height={y(from) - y(to)} style={{ fill }} />
      ))}
      {[0, 5, 7].map((v) => (
        <text key={v} x={L - 6} y={y(v) + 4} textAnchor="end">
          {v}
        </text>
      ))}
      {ticks.map((m) => (
        <text key={m} x={L + ((W - L - R) * m) / span} y={H - 7} textAnchor="middle">
          {clock(addMinutes(shiftStart, m))}
        </text>
      ))}
      {markers.map((e) => (
        <g key={`${e.kind}-${e.at}`} className="marker" transform={`translate(${x(e.at) - 8},${T - 30})`}>
          <line x1="8" x2="8" y1="20" y2={H - B - T + 30} />
          <Icon name={eventIcon[e.kind]!} />
        </g>
      ))}
      {line && <path d={line} className="line" />}
      {scored.map((p) => {
        const tier = tierOf(p);
        return (
          <circle
            key={p.at}
            data-point={p.at}
            className="dot"
            cx={x(p.at)}
            cy={y(p.total!)}
            r="4.5"
            style={{ fill: tier === null ? 'var(--muted)' : tierColour[tier] }}
          />
        );
      })}
      <line data-now x1={x(at)} x2={x(at)} y1={T - 4} y2={H - B} className="now" />
    </svg>
  );
}
