import { useT } from '../i18n/LanguageProvider';
import { addMinutes } from '../time';

export const speeds = [1, 60, 300] as const;
export type LiveSpeed = (typeof speeds)[number];

/** The clock after `elapsedMs` of real time at `speed`, never past the shift end. */
export function nextLiveAt(at: string, shiftEnd: string, speed: number, elapsedMs: number): string {
  const next = addMinutes(at, (speed * elapsedMs) / 60_000);
  return Date.parse(next) >= Date.parse(shiftEnd) ? shiftEnd : next;
}

interface LiveControlProps {
  playing: boolean;
  speed: LiveSpeed;
  atEnd: boolean;
  onPlay: () => void;
  onPause: () => void;
  onSpeed: (speed: LiveSpeed) => void;
}

/** Play / Pause the shift and pick the speed (1×, 60×, 300×). Moving the time slider pauses it (in App). */
export function LiveControl({ playing, speed, atEnd, onPlay, onPause, onSpeed }: LiveControlProps) {
  const { t } = useT();
  return (
    <div className="live" role="group" aria-label={t.liveLabel}>
      <button type="button" className={playing ? "btn" : "btn primary"} disabled={!playing && atEnd} onClick={playing ? onPause : onPlay}>
        {playing ? t.livePause : t.livePlay}
      </button>
      <div className="seg" role="group" aria-label={t.liveSpeed}>
        {speeds.map((s) => (
          <button key={s} type="button" aria-pressed={speed === s} onClick={() => onSpeed(s)}>
            <span className="mono">{s}×</span>
          </button>
        ))}
      </div>
    </div>
  );
}
