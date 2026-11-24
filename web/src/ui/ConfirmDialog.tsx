import { useEffect, useId, useRef, useState } from 'react';
import type { EscalationTier } from '../api/types';
import { useT } from '../i18n/LanguageProvider';

export interface DialogResult {
  text: string;
  tier: EscalationTier | undefined;
}

interface ConfirmDialogProps {
  /** Names the action, the bed and the patient: identity is confirmed before every action. */
  title: string;
  withNote?: boolean;
  withTier?: boolean;
  defaultTier?: EscalationTier;
  onConfirm: (result: DialogResult) => void;
  onCancel: () => void;
}

const tiers: EscalationTier[] = ['OnCallDoctor', 'SeniorDoctor', 'CriticalCare'];

/** A small confirmation dialog: the patient's name in the title, an optional note and tier, Confirm or Cancel (Esc). */
export function ConfirmDialog({ title, withNote, withTier, defaultTier = 'OnCallDoctor', onConfirm, onCancel }: ConfirmDialogProps) {
  const { t } = useT();
  const titleId = useId();
  const noteId = useId();
  const [text, setText] = useState('');
  const [tier, setTier] = useState<EscalationTier>(defaultTier);
  const first = useRef<HTMLTextAreaElement & HTMLButtonElement>(null);
  const cancel = useRef(onCancel);
  cancel.current = onCancel;

  // Once per opening: focus the first field, close on Escape, and give focus back to whatever opened the dialog.
  useEffect(() => {
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    first.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        cancel.current();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
      opener?.focus();
    };
  }, []);

  return (
    <div className="dialog-backdrop">
      <dialog open aria-modal="true" aria-labelledby={titleId} className="confirm">
        <form
          method="dialog"
          onSubmit={(e) => {
            e.preventDefault();
            onConfirm({ text: text.trim(), tier: withTier ? tier : undefined });
          }}
        >
          <h2 id={titleId}>{title}</h2>
          {withTier && (
            <fieldset>
              <legend>{t.tierLabel}</legend>
              {tiers.map((value) => (
                <label key={value}>
                  <input type="radio" name="tier" value={value} checked={tier === value} onChange={() => setTier(value)} />
                  {t[`tier${value}`]}
                </label>
              ))}
            </fieldset>
          )}
          {withNote && (
            <label htmlFor={noteId} className="note-field">
              {t.noteLabel}
              <textarea id={noteId} ref={first} rows={3} value={text} onChange={(e) => setText(e.target.value)} />
            </label>
          )}
          <div className="dialog-actions">
            <button type="button" className="btn" onClick={onCancel}>
              {t.cancel}
            </button>
            <button type="submit" className="btn primary" ref={withNote ? undefined : first}>
              {t.confirm}
            </button>
          </div>
        </form>
      </dialog>
    </div>
  );
}
