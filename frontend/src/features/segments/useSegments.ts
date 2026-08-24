import { useCallback, useEffect, useState } from 'react';

import { ApiError, listSegments, type SegmentSummary } from './api';

export type SegmentsState =
  | { status: 'loading' }
  | { status: 'ready'; segments: SegmentSummary[] }
  | { status: 'failed'; message: string };

export interface SegmentsResult {
  state: SegmentsState;
  /** Re-reads the list. Used by the retry link and after a segment is created. */
  reload: () => void;
}

/**
 * Every segment. No environment here, unlike `useFlags`: a segment's definition is global, and only
 * which flags point at it varies by environment.
 */
export function useSegments(): SegmentsResult {
  const [state, setState] = useState<SegmentsState>({ status: 'loading' });
  const [reloadCount, setReloadCount] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    // Resets the previous result before the new fetch starts — the documented React
    // fetch-on-effect pattern (react.dev/learn/synchronizing-with-effects#fetching-data).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setState({ status: 'loading' });

    listSegments(controller.signal)
      .then((segments) => {
        if (!controller.signal.aborted) {
          setState({ status: 'ready', segments });
        }
      })
      .catch((cause: unknown) => {
        // Aborted means a newer request is already on its way, or the screen is gone. Either way
        // this answer is no longer wanted, and painting an error over it would be wrong.
        if (controller.signal.aborted) {
          return;
        }

        setState({
          status: 'failed',
          message:
            cause instanceof ApiError ? cause.message : 'The console could not reach the API.',
        });
      });

    return () => controller.abort();
  }, [reloadCount]);

  const reload = useCallback(() => setReloadCount((count) => count + 1), []);

  return { state, reload };
}
