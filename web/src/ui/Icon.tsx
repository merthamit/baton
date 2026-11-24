// Baton's icon set: authored 24×24 SVG, one 1.75 stroke, drawn in currentColor. Decorative by default; the text next
// to an icon always carries the meaning.

const paths = {
  flag: 'M5 21V4M5 4h11l-2.5 4L16 12H5',
  phone:
    'M6.6 3.5h3l1.6 4.2-2 1.3a11 11 0 0 0 5.8 5.8l1.3-2 4.2 1.6v3a2 2 0 0 1-2.2 2A16.5 16.5 0 0 1 4.6 5.7a2 2 0 0 1 2-2.2z',
  doctor: 'M6 3v5a4 4 0 0 0 8 0V3M10 12v3.5a4.5 4.5 0 0 0 9 0V13M19 13a2 2 0 1 0 0-4 2 2 0 0 0 0 4z',
  critical: 'M7 18v-5a5 5 0 0 1 10 0v5M4.5 21h15M5 18h14M12 3v2M4.6 6.6l1.4 1.4M19.4 6.6 18 8',
  clock: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 7.5V12l3 2',
  drop: 'M12 3.5s6 6.4 6 10.5a6 6 0 0 1-12 0c0-4.1 6-10.5 6-10.5z',
  bell: 'M6 16v-5a6 6 0 0 1 12 0v5l1.5 2h-15zM10 21h4',
  note: 'M6 3h9l3 3v15H6zM9 10h6M9 14h6M9 18h4',
  lungs: 'M12 4v7M12 11l-3 2M12 11l3 2M8.5 7C6 7 4 11 4 15.5 4 18 5 19.5 7 19.5S10 18 10 15.5V9M15.5 7C18 7 20 11 20 15.5c0 2.5-1 4-3 4S14 18 14 15.5V9',
} as const;

export type IconName = keyof typeof paths;

export function Icon({ name, label }: { name: IconName; label?: string }) {
  return (
    <svg
      className="icon"
      viewBox="0 0 24 24"
      width="16"
      height="16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden={label ? undefined : true}
      role={label ? 'img' : undefined}
      aria-label={label}
      focusable="false"
    >
      <path d={paths[name]} />
    </svg>
  );
}

export type ShapeName = 'circle' | 'triangle' | 'diamond' | 'square' | 'unknown';

/** The risk tier's shape, so risk never depends on colour alone. */
export function Shape({ shape }: { shape: ShapeName }) {
  return (
    <svg className="shape" data-shape={shape} viewBox="0 0 16 16" width="14" height="14" aria-hidden="true" focusable="false">
      {shape === 'circle' && <circle cx="8" cy="8" r="5.5" fill="currentColor" />}
      {shape === 'triangle' && <path d="M8 2.2 14 13.5H2z" fill="currentColor" />}
      {shape === 'diamond' && <path d="M8 1.5 14.5 8 8 14.5 1.5 8z" fill="currentColor" />}
      {shape === 'square' && <rect x="2.5" y="2.5" width="11" height="11" fill="currentColor" />}
      {shape === 'unknown' && <circle cx="8" cy="8" r="5.5" fill="none" stroke="currentColor" strokeWidth="1.6" strokeDasharray="2.4 2" />}
    </svg>
  );
}
