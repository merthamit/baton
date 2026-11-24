import { useId } from 'react';
import type { WardEvent } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { clock, minutesBetween } from '../time';

interface EventTimelineProps {
  events: WardEvent[];
  available: boolean;
  shiftStart: string;
}

/** What happened around the patient, oldest first. Kind labels are translated; the event text stays as stored. */
export function EventTimeline({ events, available, shiftStart }: EventTimelineProps) {
  const { t } = useT();
  const id = useId();

  return (
    <div className="timeline">
      <h3 id={id}>{t.events}</h3>
      {!available ? (
        <p className="muted">{t.eventsUnavailable}</p>
      ) : events.length === 0 ? (
        <p className="muted">{t.noEvents}</p>
      ) : (
        <ol aria-labelledby={id}>
          {events.map((e) => (
            <li key={`${e.kind}-${e.at}`} data-kind={e.kind}>
              <span className="tm mono">{minutesBetween(shiftStart, e.at) < 0 ? t.beforeShift : clock(e.at)}</span>
              <span>
                <b>{t[`event${e.kind}`]}</b> <span className="muted">{e.text}</span>
              </span>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
