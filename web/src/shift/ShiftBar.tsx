import { useEffect, useState } from 'react';
import type { ShiftState } from '../api/types';
import { useT } from '../i18n/LanguageProvider';

const speeds = [1, 10, 60];

/** The ward clock between server answers: the last ward time plus the real time since, at the shift's speed. */
export function wardNow(state: ShiftState, fetchedAt: number, realNow: number): number {
  const now = Date.parse(state.now) + Math.max(0, realNow - fetchedAt) * state.speed;
  return Math.min(now, Date.parse(state.shiftEnd));
}

function clockWithSeconds(ms: number, offset: string): string {
  // Shown in the ward's own offset, taken from the server's ISO string (e.g. +03:00).
  const match = /([+-])(\d{2}):(\d{2})$/.exec(offset);
  const shift = match ? (match[1] === '-' ? -1 : 1) * (Number(match[2]) * 60 + Number(match[3])) : 0;
  const d = new Date(ms + shift * 60_000);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}`;
}

interface ShiftBarProps {
  state: ShiftState;
  fetchedAt: number;
  role: 'nurse' | 'doctor';
  otherRoleHref: string;
  onRole: (role: 'nurse' | 'doctor') => void;
  onSpeed: (speed: number) => void;
  onEnd: () => void;
}

/** The live shift's banner: the running ward clock, its speed, the role this tab plays, and the other role's tab. */
export function ShiftBar({ state, fetchedAt, role, otherRoleHref, onRole, onSpeed, onEnd }: ShiftBarProps) {
  const { t } = useT();
  const [, tick] = useState(0);
  useEffect(() => {
    const timer = setInterval(() => tick((n) => n + 1), 1000);
    return () => clearInterval(timer);
  }, []);
  const now = wardNow(state, fetchedAt, Date.now());

  return (
    <section className="try-banner shift-bar" aria-label={t.shiftBanner}>
      <div className="shift-clock">
        <span className="live-dot" aria-hidden="true" />
        <strong>{t.shiftBanner}</strong>
        <span className="mono" aria-hidden="true">
          {clockWithSeconds(now, state.now)}
        </span>
        <span className="visually-hidden">{t.shiftClock(clockWithSeconds(now, state.now).slice(0, 5))}</span>
      </div>
      <div className="seg" role="group" aria-label={t.shiftSpeed}>
        {speeds.map((s) => (
          <button key={s} type="button" aria-pressed={state.speed === s} onClick={() => onSpeed(s)}>
            {s}×
          </button>
        ))}
      </div>
      <div className="seg" role="group" aria-label={t.shiftRoleLabel}>
        <button type="button" aria-pressed={role === 'nurse'} onClick={() => onRole('nurse')}>
          {t.roleTabNurse}
        </button>
        <button type="button" aria-pressed={role === 'doctor'} onClick={() => onRole('doctor')}>
          {t.roleTabDoctor}
        </button>
      </div>
      <a className="btn" href={otherRoleHref} target="_blank" rel="noopener">
        {role === 'nurse' ? t.shiftOpenDoctor : t.shiftOpenNurse}
      </a>
      <button type="button" className="btn" onClick={onEnd}>
        {t.shiftEnd}
      </button>
      <p className="shift-note muted">{t.shiftNote}</p>
    </section>
  );
}
