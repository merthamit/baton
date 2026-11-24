import type { Messages } from '../i18n/en';

/**
 * A Try refusal in the interface language. The API sends a stable code as the problem type
 * (urn:baton:try:{code}) and English detail; an unknown code falls back to the server's text.
 */
export function refusalText(type: string | undefined, detail: string, t: Messages, lastActionClock: string): string {
  switch (type?.replace('urn:baton:try:', '')) {
    case 'unknown':
      return t.tryRefusalUnknown;
    case 'too-many':
      return t.tryRefusalTooMany;
    case 'note-too-long':
      return t.tryRefusalNoteTooLong;
    case 'before-last':
      return t.tryRefusalBeforeLast(lastActionClock);
    case 'outside-shift':
      return t.tryRefusalOutsideShift;
    case 'no-arrival':
      return t.tryRefusalNoArrival;
    case 'assessment-first':
      return t.tryRefusalAssessmentFirst;
    default:
      return detail;
  }
}
