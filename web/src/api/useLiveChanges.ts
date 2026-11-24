import { useEffect, useRef, useState } from 'react';

/** What a live event says: something changed for this patient (or the ward), refetch. No clinical content. */
export interface WardChange {
  kind: string;
  patientId: string | null;
  trySession: string | null;
  at: string;
}

export type LiveStatus = 'connecting' | 'open' | 'reconnecting';

/** Accepts camelCase or PascalCase JSON, so a serializer setting on the server never silences the board. */
function read(data: string): WardChange | null {
  try {
    const raw = JSON.parse(data) as Record<string, unknown>;
    const pick = (name: string) => (raw[name] ?? raw[name[0]!.toUpperCase() + name.slice(1)] ?? null) as string | null;
    return { kind: pick('kind') ?? '', patientId: pick('patientId'), trySession: pick('trySession'), at: pick('at') ?? '' };
  } catch {
    return null;
  }
}

/**
 * Listens to the API's live stream (/api/live, Server-Sent Events) and calls `onChange` for each change, except
 * changes inside another Try session. The browser reconnects by itself; the status says how the link is.
 */
export function useLiveChanges(onChange: (change: WardChange) => void, trySession?: string): LiveStatus {
  const [status, setStatus] = useState<LiveStatus>('connecting');
  const handler = useRef(onChange);
  handler.current = onChange;
  const session = useRef(trySession);
  session.current = trySession;

  useEffect(() => {
    if (typeof EventSource === 'undefined') {
      return undefined;
    }
    const source = new EventSource('/api/live');
    source.onopen = () => setStatus('open');
    source.onerror = () => setStatus('reconnecting');
    source.addEventListener('ready', () => setStatus('open'));
    source.addEventListener('change', (e: MessageEvent<string>) => {
      const change = read(e.data);
      if (!change || (change.trySession && change.trySession !== session.current)) {
        return;
      }
      setStatus('open');
      handler.current(change);
    });
    return () => source.close();
  }, []);

  return status;
}
