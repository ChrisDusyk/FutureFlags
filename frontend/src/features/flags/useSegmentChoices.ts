import { useEffect, useState } from 'react';

import { ApiError, listSegments, type SegmentSummary } from '../segments/api';

export type SegmentChoicesState =
  | { status: 'loading' }
  | { status: 'ready'; segments: SegmentSummary[] }
  | { status: 'failed'; message: string };

/**
 * Every segment, for the targeting editors on the flag detail page.
 *
 * Loaded once for the page rather than once per environment: three editors asking the same
 * question three times would be three identical requests for one answer that cannot differ
 * between them, since a segment's definition is global.
 */
export function useSegmentChoices(): SegmentChoicesState {
  const [state, setState] = useState<SegmentChoicesState>({ status: 'loading' });

  useEffect(() => {
    const controller = new AbortController();

    listSegments(controller.signal)
      .then((segments) => {
        if (!controller.signal.aborted) {
          setState({ status: 'ready', segments });
        }
      })
      .catch((cause: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setState({
          status: 'failed',
          message:
            cause instanceof ApiError ? cause.message : 'The console could not read the segments.',
        });
      });

    return () => controller.abort();
  }, []);

  return state;
}
