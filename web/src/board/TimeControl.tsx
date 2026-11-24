import { useEffect, useId, useRef, useState } from 'react';
import { useT } from '../i18n/LanguageProvider';
import { addMinutes, clock, minutesBetween } from '../time';

interface TimeControlProps {
  shiftStart: string;
  shiftEnd: string;
  at: string;
  onChange: (at: string) => void;
  /** Called at once when the user moves the slider (before the debounced onChange). */
  onUserMove?: () => void;
  /** While the shift plays, the clock is not announced on every tick. */
  quiet?: boolean;
}

const stepMinutes = 15;
const debounceMs = 150;

/** A slider over the shift in 15-minute steps. The clock follows at once; the board reloads after a short pause. */
export function TimeControl({ shiftStart, shiftEnd, at, onChange, onUserMove, quiet }: TimeControlProps) {
  const { t } = useT();
  const id = useId();
  const max = Math.round(minutesBetween(shiftStart, shiftEnd) / stepMinutes);
  const toStep = (iso: string) => Math.min(max, Math.max(0, Math.round(minutesBetween(shiftStart, iso) / stepMinutes)));
  const [step, setStep] = useState(() => toStep(at));
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  // Follow a new moment from outside (URL, first answer); toStep depends only on these props.
  useEffect(() => setStep(toStep(at)), [at, shiftStart, shiftEnd]);
  useEffect(() => () => clearTimeout(timer.current), []);

  // While dragging the label follows the slider; otherwise it is the board's own moment (the live clock is not on
  // 15-minute steps, and the label must never show another time than the cards).
  const shown = step === toStep(at) ? at : addMinutes(shiftStart, step * stepMinutes);

  return (
    <div className="time">
      <label htmlFor={id} className="lbl">
        {t.timeLabel}
      </label>
      <div className="time-row">
        <input
          id={id}
          type="range"
          min={0}
          max={max}
          step={1}
          value={step}
          aria-valuetext={clock(shown)}
          onChange={(e) => {
            const next = Number(e.target.value);
            onUserMove?.();
            setStep(next);
            clearTimeout(timer.current);
            timer.current = setTimeout(() => onChange(addMinutes(shiftStart, next * stepMinutes)), debounceMs);
          }}
        />
        <output htmlFor={id} className="clock mono" aria-live={quiet ? 'off' : undefined}>
          {clock(shown)}
        </output>
      </div>
    </div>
  );
}
