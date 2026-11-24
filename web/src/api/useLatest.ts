import { useCallback, useEffect, useRef, useState, type DependencyList } from 'react';

export interface Latest<T> {
  data?: T;
  error?: Error;
  loading: boolean;
  retry: () => void;
}

/**
 * Runs `load` whenever `deps` change. The previous request is aborted and any answer that is not from the latest
 * request is ignored, so a slow old answer can never replace a newer one (fast slider moves). Data from the last
 * success stays visible while the next request is loading, unless `identity` changed: data about something else
 * (another patient) is dropped at once, never shown under the new selection.
 */
export function useLatest<T>(load: (signal: AbortSignal) => Promise<T>, deps: DependencyList, identity?: unknown): Latest<T> {
  const [state, setState] = useState<{ data?: T; error?: Error; loading: boolean }>({ loading: true });
  const [attempt, setAttempt] = useState(0);
  const latest = useRef(0);
  const shownIdentity = useRef(identity);

  useEffect(() => {
    const id = ++latest.current;
    const controller = new AbortController();
    if (shownIdentity.current !== identity) {
      shownIdentity.current = identity;
      setState({ loading: true });
    } else {
      setState((s) => ({ ...s, loading: true }));
    }

    load(controller.signal).then(
      (data) => {
        if (id === latest.current) {
          setState({ data, loading: false });
        }
      },
      (error: unknown) => {
        if (id === latest.current && !controller.signal.aborted) {
          setState((s) => ({ data: s.data, error: error instanceof Error ? error : new Error(String(error)), loading: false }));
        }
      },
    );

    return () => controller.abort();
  }, [...deps, attempt]);

  const retry = useCallback(() => setAttempt((a) => a + 1), []);
  return { ...state, retry };
}
