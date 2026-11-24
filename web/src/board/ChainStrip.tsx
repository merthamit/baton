import { useId } from 'react';
import type { EscalationState, EscalationStep } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { clock, minutesBetween } from '../time';
import { Icon } from '../ui/Icon';

const steps: Exclude<EscalationStep, 'None'>[] = ['Called', 'Arrived', 'Assessed', 'Planned'];

/**
 * The card's escalation strip: the four steps of the latest call as labelled text (never colour or dots alone), the
 * open call's timer against its target, and the API's reminder when the target has passed or a higher tier is due.
 */
export function ChainStrip({ state }: { state: EscalationState }) {
  const { t } = useT();
  const id = useId();
  if (state.step === 'None' && !state.suggestedNextTier) {
    return null;
  }

  const reached = steps.indexOf(state.step as (typeof steps)[number]);
  const label = { Called: t.stepCalled, Arrived: t.stepArrived, Assessed: t.stepAssessed, Planned: t.stepPlanned };
  const latest = state.calls.at(-1);
  const open = state.openCall;
  const tierName = (tier: string) => t[`tier${tier}` as 'tierOnCallDoctor'];

  let reminder: string | null = null;
  if (state.targetPassed && state.suggestedNextTier) {
    reminder = `${t.targetPassed} · ${state.suggestedNextTier === 'CriticalCare' ? t.suggestCriticalCare : t.suggestSeniorDoctor}`;
  } else if (state.targetPassed) {
    reminder = t.targetPassed;
  } else if (state.suggestedNextTier) {
    reminder = state.suggestedNextTier === 'CriticalCare' ? t.suggestCriticalCareAlone : t.suggestSeniorDoctorAlone;
  }

  return (
    <span className="chain">
      {state.step !== 'None' && (
        <ol className="chain-steps" aria-label={t.chainLabel} id={id}>
          {steps.map((step, i) => {
            const done = i <= reached;
            return (
              <li key={step} data-state={done ? 'done' : 'todo'}>
                <span className="chain-mark" aria-hidden="true" />
                {label[step]}
                <span className="visually-hidden"> ({done ? t.stepDone : t.stepTodo})</span>
              </li>
            );
          })}
        </ol>
      )}
      {latest && (
        <span className="chain-meta">
          <span>{t.calledAt(tierName(latest.tier), clock(latest.calledAt))}</span>
          {open && state.minutesWaited !== null && (
            <span className="chain-timer mono" data-passed={state.targetPassed || undefined}>
              <Icon name="clock" />
              <span>
                {open.targetAt
                  ? t.timer(state.minutesWaited, minutesBetween(open.calledAt, open.targetAt))
                  : t.waited(state.minutesWaited)}
              </span>
            </span>
          )}
        </span>
      )}
      {reminder && (
        <span className="chain-reminder" role="note">
          {reminder}
        </span>
      )}
    </span>
  );
}
