import type { Consciousness, News2Input, News2Parameter, RiskLevel, ScorePoint } from './api/types';
import type { Messages } from './i18n/en';
import type { ShapeName } from './ui/Icon';

/** Display tier from the API's risk (no thresholds here): 0 score 0, 1 score 1–4, 2 single 3 or 5–6, 3 seven or more. */
export type Tier = 0 | 1 | 2 | 3;

export function tierOf(point: ScorePoint | null): Tier | null {
  return point ? tierOfRisk(point.risk, point.total) : null;
}

/**
 * For an incomplete set (no risk), the least tier its known parts already show: a minimum total of 7 or more, or of
 * 5–6 or a parameter scoring 3. Null when nothing known raises it.
 */
export function floorTierOf(point: ScorePoint | null): Tier | null {
  if (!point || point.risk !== null) {
    return null;
  }
  if ((point.minimumTotal ?? 0) >= 7) {
    return 3;
  }
  return (point.minimumTotal ?? 0) >= 5 || point.parametersScoring3.length > 0 ? 2 : null;
}

export function tierOfRisk(risk: RiskLevel | null, total: number | null): Tier | null {
  if (risk === null) {
    return null;
  }
  if (risk === 'High') {
    return 3;
  }
  if (risk === 'Medium' || risk === 'LowMedium') {
    return 2;
  }
  return (total ?? 0) >= 1 ? 1 : 0;
}

export function riskName(risk: RiskLevel | null, t: Messages): string {
  switch (risk) {
    case 'High':
      return t.riskHigh;
    case 'Medium':
      return t.riskMedium;
    case 'LowMedium':
      return t.riskLowMedium;
    case 'Low':
      return t.riskLow;
    default:
      return t.incompleteScore;
  }
}

export const shapeOf = (tier: Tier | null): ShapeName =>
  tier === null ? 'unknown' : (['circle', 'triangle', 'diamond', 'square'] as const)[tier];

export function riskText(point: ScorePoint, t: Messages): string {
  return riskName(point.risk, t);
}

export function parameterLabel(parameter: News2Parameter, t: Messages): string {
  return t[`param${parameter}`];
}

const acvpu: Record<Consciousness, string> = { Alert: 'A', Confusion: 'C', Voice: 'V', Pain: 'P', Unresponsive: 'U' };

/** The reading behind a parameter, as a nurse writes it on the chart. */
export function readingOf(parameter: News2Parameter, input: News2Input, t: Messages): string | null {
  switch (parameter) {
    case 'RespirationRate':
      return input.respirationRate?.toString() ?? null;
    case 'SpO2':
      return input.spO2 === null ? null : `${input.spO2}%`;
    case 'SupplementalOxygen':
      return input.onSupplementalOxygen === null ? null : input.onSupplementalOxygen ? t.onOxygen : t.roomAir;
    case 'SystolicBp':
      return input.systolicBp?.toString() ?? null;
    case 'PulseRate':
      return input.pulseRate?.toString() ?? null;
    case 'Consciousness':
      return input.consciousness === null ? null : acvpu[input.consciousness];
    case 'Temperature':
      return input.temperatureCelsius === null ? null : input.temperatureCelsius.toFixed(1);
  }
}
