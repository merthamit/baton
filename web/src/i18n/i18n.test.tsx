import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { en } from './en';
import { tr } from './tr';
import { LanguageProvider, LanguageToggle, useT } from './LanguageProvider';

function Notice() {
  const { t } = useT();
  return <p>{t.syntheticNotice}</p>;
}

function renderWithLanguage() {
  return render(
    <LanguageProvider>
      <LanguageToggle />
      <Notice />
    </LanguageProvider>,
  );
}

describe('dictionaries', () => {
  it('English and Turkish have the same keys', () => {
    expect(Object.keys(tr).sort()).toEqual(Object.keys(en).sort());
  });
});

describe('language toggle', () => {
  it('starts in English and switches text and the page language to Turkish', async () => {
    renderWithLanguage();
    expect(screen.getByText(en.syntheticNotice)).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('en');

    await userEvent.click(screen.getByRole('button', { name: 'Türkçe' }));

    expect(screen.getByText(tr.syntheticNotice)).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('tr');
  });

  it('keeps working when storage throws', async () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    renderWithLanguage();
    await userEvent.click(screen.getByRole('button', { name: 'Türkçe' }));

    expect(screen.getByText(tr.syntheticNotice)).toBeInTheDocument();
  });
});
