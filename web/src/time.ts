// Ward time helpers. Timestamps from the API carry the ward's offset (for example +03:00); the board shows that
// local time whatever zone the browser is in, so these work on the offset in the string, never on Date's local zone.

const ISO = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.\d+)?)?(Z|[+-]\d{2}:\d{2})$/;

interface Parts {
  /** Milliseconds since the epoch (the instant). */
  instant: number;
  /** Offset from UTC in minutes. */
  offsetMinutes: number;
  /** The offset exactly as written ("Z" or "+03:00"). */
  offset: string;
}

function parse(iso: string): Parts {
  const m = ISO.exec(iso);
  if (!m) {
    throw new Error(`Not an ISO 8601 timestamp with an offset: ${iso}`);
  }

  const [, y, mo, d, h, mi, s, offset = 'Z'] = m;
  const offsetMinutes =
    offset === 'Z' ? 0 : (offset.startsWith('-') ? -1 : 1) * (Number(offset.slice(1, 3)) * 60 + Number(offset.slice(4, 6)));
  const localAsUtc = Date.UTC(Number(y), Number(mo) - 1, Number(d), Number(h), Number(mi), Number(s ?? 0));
  return { instant: localAsUtc - offsetMinutes * 60_000, offsetMinutes, offset };
}

const pad = (n: number) => String(n).padStart(2, '0');

/** The wall-clock fields of an instant in a given offset. */
function local(instant: number, offsetMinutes: number): Date {
  return new Date(instant + offsetMinutes * 60_000);
}

/** Adds minutes and writes the result in the same offset, to the second. */
export function addMinutes(iso: string, minutes: number): string {
  const p = parse(iso);
  const t = local(p.instant + minutes * 60_000, p.offsetMinutes);
  return (
    `${t.getUTCFullYear()}-${pad(t.getUTCMonth() + 1)}-${pad(t.getUTCDate())}` +
    `T${pad(t.getUTCHours())}:${pad(t.getUTCMinutes())}:${pad(t.getUTCSeconds())}${p.offset}`
  );
}

/** "HH:mm" in the timestamp's own offset. */
export function clock(iso: string): string {
  const p = parse(iso);
  const t = local(p.instant, p.offsetMinutes);
  return `${pad(t.getUTCHours())}:${pad(t.getUTCMinutes())}`;
}

/** Whole minutes from one instant to another (negative when `to` is earlier). */
export function minutesBetween(fromIso: string, toIso: string): number {
  return Math.round((parse(toIso).instant - parse(fromIso).instant) / 60_000);
}
