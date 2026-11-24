import type { WardResponse } from '../api/types';
import { useT } from '../i18n/LanguageProvider';
import { BedCard } from './BedCard';

interface WardBoardProps {
  ward: WardResponse;
  selectedId: string | undefined;
  onSelect: (patientId: string) => void;
}

/** Beds in bed order. Positions never re-sort by risk: nurses find a bed where it always is. */
export function WardBoard({ ward, selectedId, onSelect }: WardBoardProps) {
  const { t } = useT();
  const beds = [...ward.beds].sort((a, b) => (a.bed ?? a.patientId).localeCompare(b.bed ?? b.patientId, undefined, { numeric: true }));

  return (
    <section className="board" aria-label={t.boardLabel}>
      {beds.length === 0 ? (
        <p className="empty">{t.emptyWard}</p>
      ) : (
        beds.map((bed) => <BedCard key={bed.patientId} bed={bed} selected={bed.patientId === selectedId} onSelect={onSelect} />)
      )}
    </section>
  );
}

/** Placeholder cards while the first answer is on its way. */
export function BoardSkeleton() {
  const { t } = useT();
  return (
    <section className="board" aria-busy="true" aria-label={t.loading}>
      {Array.from({ length: 8 }, (_, i) => (
        <span key={i} className="bed skeleton" aria-hidden="true" />
      ))}
    </section>
  );
}
