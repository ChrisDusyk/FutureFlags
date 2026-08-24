import { useCallback, useEffect, useState } from 'react';

import {
  ApiError,
  getSegment,
  getSegmentHistory,
  type SegmentDetail,
  type SegmentHistoryEntry,
} from './api';

export type SegmentDetailState =
  | { status: 'loading' }
  | { status: 'ready'; segment: SegmentDetail }
  | { status: 'missing' }
  | { status: 'failed'; message: string };

export type SegmentHistoryState =
  | { status: 'loading' }
  | { status: 'ready'; entries: SegmentHistoryEntry[] }
  | { status: 'failed'; message: string };

export interface SegmentDetailResult {
  detail: SegmentDetailState;
  history: SegmentHistoryState;
  reload: () => void;
}

/**
 * One segment and its activity, fetched independently.
 *
 * Two effects rather than one await of both: the definition is what somebody came here to edit, and
 * making it wait on a history query that has nothing to do with it would be a slower screen for no
 * reason. They share a reload counter so a save refreshes both.
 */
export function useSegmentDetail(key: string): SegmentDetailResult {
  const [detail, setDetail] = useState<SegmentDetailState>({ status: 'loading' });
  const [history, setHistory] = useState<SegmentHistoryState>({ status: 'loading' });
  const [reloadCount, setReloadCount] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    // eslint-disable-next-line react-hooks/set-state-in-effect
    setDetail({ status: 'loading' });

    getSegment(key, controller.signal)
      .then((segment) => {
        if (!controller.signal.aborted) {
          setDetail({ status: 'ready', segment });
        }
      })
      .catch((cause: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        // A missing segment is an answer rather than a failure — somebody followed an old link, or
        // it was retired — and it deserves its own screen rather than an error banner.
        if (cause instanceof ApiError && cause.status === 404) {
          setDetail({ status: 'missing' });

          return;
        }

        setDetail({
          status: 'failed',
          message:
            cause instanceof ApiError ? cause.message : 'The console could not reach the API.',
        });
      });

    return () => controller.abort();
  }, [key, reloadCount]);

  useEffect(() => {
    const controller = new AbortController();

    // eslint-disable-next-line react-hooks/set-state-in-effect
    setHistory({ status: 'loading' });

    getSegmentHistory(key, controller.signal)
      .then((entries) => {
        if (!controller.signal.aborted) {
          setHistory({ status: 'ready', entries });
        }
      })
      .catch((cause: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setHistory({
          status: 'failed',
          message:
            cause instanceof ApiError ? cause.message : 'The console could not read the history.',
        });
      });

    return () => controller.abort();
  }, [key, reloadCount]);

  const reload = useCallback(() => setReloadCount((count) => count + 1), []);

  return { detail, history, reload };
}
