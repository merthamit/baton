import { useEffect, useId, useRef, useState } from 'react';
import type { SourceRef } from '../api/types';
import { useT } from '../i18n/LanguageProvider';

/**
 * A small "Source" button after a sentence. It opens a list of the records the sentence was built from (label and
 * FHIR reference), grouped by label; Esc closes it and focus goes back to the button.
 */
export function SourceButton({ sources }: { sources: SourceRef[] }) {
  const { t } = useT();
  const [open, setOpen] = useState(false);
  const id = useId();
  const button = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(false);
        button.current?.focus();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open]);

  const groups = new Map<string, string[]>();
  for (const source of sources) {
    groups.set(source.label, [...(groups.get(source.label) ?? []), source.reference]);
  }

  return (
    <span className="source">
      <button ref={button} type="button" className="source-btn" aria-expanded={open} aria-controls={id} onClick={() => setOpen((o) => !o)}>
        {t.source}
      </button>
      {open && (
        <span id={id} className="source-pop" role="group" aria-label={t.sourceListLabel}>
          {[...groups].map(([label, references]) => (
            <span key={label} className="source-group">
              <span className="source-label">{label}</span>
              {references.map((reference) => (
                <span key={reference} className="source-ref">
                  {reference}
                </span>
              ))}
            </span>
          ))}
        </span>
      )}
    </span>
  );
}
