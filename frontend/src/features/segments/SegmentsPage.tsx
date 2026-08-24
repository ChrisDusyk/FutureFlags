import { useState } from 'react';

import { PageHeader } from '../../shell/PageHeader';
import { NewSegmentDialog } from './NewSegmentDialog';
import { SegmentRow } from './SegmentRow';
import { useSegments } from './useSegments';

export function SegmentsPage() {
  const { state, reload } = useSegments();
  const [creating, setCreating] = useState(false);

  return (
    <>
      <PageHeader
        eyebrow="Delivery"
        title="Segments"
        lede="Named groups of people a feature can reach — beta testers, internal staff, one account you are debugging."
      />

      <div className="flagbar">
        <p className="flagbar__count">
          {state.status === 'ready'
            ? `${state.segments.length} ${state.segments.length === 1 ? 'segment' : 'segments'}`
            : ' '}
        </p>
        <button type="button" className="button" onClick={() => setCreating(true)}>
          New segment
        </button>
      </div>

      {state.status === 'loading' && (
        <p className="flaglist__note" role="status">
          Reading the segments…
        </p>
      )}

      {state.status === 'failed' && (
        <div className="flaglist__failed" role="alert">
          <p>{state.message}</p>
          <button type="button" className="textlink" onClick={reload}>
            Try again
          </button>
        </div>
      )}

      {/*
        An empty list is not an unbuilt screen. This feature works — there is simply nothing in it
        yet, and saying so plainly beats dressing the state up as something missing.
      */}
      {state.status === 'ready' && state.segments.length === 0 && (
        <div className="flaglist__empty">
          <h2 className="flaglist__emptytitle">No segments yet.</h2>
          <p className="flaglist__emptybody">
            A segment is a group defined once and pointed at by any flag that needs the same
            audience — built from the traits your app already sends when it evaluates a flag. Until
            a flag targets one, nothing changes for anybody.
          </p>
        </div>
      )}

      {state.status === 'ready' && state.segments.length > 0 && (
        <ul className="seglist">
          {state.segments.map((segment) => (
            <SegmentRow key={segment.id} segment={segment} />
          ))}
        </ul>
      )}

      {creating && (
        <NewSegmentDialog
          onClose={() => setCreating(false)}
          onCreated={() => {
            setCreating(false);
            reload();
          }}
        />
      )}
    </>
  );
}
