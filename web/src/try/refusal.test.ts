import { describe, expect, it } from 'vitest';
import { refusalText } from './refusal';
import { en } from '../i18n/en';
import { tr } from '../i18n/tr';

describe('refusalText', () => {
  it('translates a Try refusal by its code', () => {
    expect(refusalText('urn:baton:try:no-arrival', 'The doctor you called has not arrived yet', tr, '23:40')).toBe(tr.tryRefusalNoArrival);
    expect(refusalText('urn:baton:try:before-last', 'x', tr, '23:40')).toBe(tr.tryRefusalBeforeLast('23:40'));
    expect(refusalText('urn:baton:try:assessment-first', 'x', en, '23:40')).toBe(en.tryRefusalAssessmentFirst);
  });

  it('falls back to the server text for an unknown code', () => {
    expect(refusalText(undefined, 'Something else', tr, '23:40')).toBe('Something else');
  });
});
