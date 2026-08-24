import { useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';

import { PageHeader } from '../../shell/PageHeader';
import { environments } from '../../shell/environment';
import { ApiError, updateFlag, type FlagDetail, type FlagHistoryEntry } from './api';
import { changedAgo, changedExactly } from './changed';
import { TargetingEditor } from './TargetingEditor';
import { useFlagDetail } from './useFlagDetail';
import { useSegmentChoices } from './useSegmentChoices';

function summarize(entry: FlagHistoryEntry): string {
  switch (entry.eventType) {
    case 'FlagCreated':
      return 'Created';
    case 'FlagDetailsChanged':
      return 'Name or description updated';
    case 'FlagStateChanged':
      return `Turned ${entry.isEnabled ? 'on' : 'off'} in ${entry.environment}`;
    case 'FlagTargetingChanged':
      return describeTargeting(entry);
  }
}

function describeTargeting(entry: FlagHistoryEntry): string {
  const segments = entry.targetedSegments ?? [];

  // "Reaches everyone" rather than "targets nothing": the empty list is not an absence, it is the
  // answer a flag gave before segments existed and still gives now.
  return segments.length === 0
    ? `Now reaches everyone in ${entry.environment}`
    : `Now reaches ${segments.join(', ')} in ${entry.environment}`;
}

function EditFlagForm({
  flag,
  onCancel,
  onSaved,
}: {
  flag: FlagDetail;
  onCancel: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState(flag.name);
  const [description, setDescription] = useState(flag.description);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await updateFlag(flag.key, { name: name.trim(), description: description.trim() });
      onSaved();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : 'The console could not save this flag.');
      setSubmitting(false);
    }
  }

  return (
    <form className="flagdetail__form" onSubmit={(event) => void handleSubmit(event)} noValidate>
      <label className="field">
        <span className="field__label">Name</span>
        <input
          className="field__input"
          value={name}
          onChange={(event) => setName(event.target.value)}
          autoComplete="off"
          required
        />
      </label>

      <label className="field">
        <span className="field__label">Description</span>
        <textarea
          className="field__input"
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          rows={3}
        />
      </label>

      {error && (
        <p className="field__error" role="alert">
          {error}
        </p>
      )}

      <div className="dialog__actions">
        <button type="button" className="button button--quiet" onClick={onCancel} disabled={submitting}>
          Cancel
        </button>
        <button className="button" type="submit" disabled={submitting}>
          {submitting ? 'Saving…' : 'Save'}
        </button>
      </div>
    </form>
  );
}

function BackToFlagsLink() {
  return (
    <Link className="flagdetail__back" to="/flags">
      ← All flags
    </Link>
  );
}

export function FlagDetailPage() {
  const { key = '' } = useParams();
  const { detail, history, reload } = useFlagDetail(key);
  const segments = useSegmentChoices();
  const [editing, setEditing] = useState(false);

  if (detail.status === 'loading') {
    return (
      <>
        <BackToFlagsLink />
        <p className="flaglist__note" role="status">
          Reading {key}…
        </p>
      </>
    );
  }

  if (detail.status === 'failed') {
    return (
      <>
        <BackToFlagsLink />
        <div className="flaglist__failed" role="alert">
          <p>{detail.message}</p>
        </div>
      </>
    );
  }

  const { flag } = detail;

  return (
    <>
      <BackToFlagsLink />
      <PageHeader eyebrow="Flags" title={flag.name} lede={`Details and activity for ${flag.key}.`} />

      <p className="flagdetail__key">
        <span className="flagdetail__keylabel">Key</span> <code>{flag.key}</code>
      </p>

      {editing ? (
        <EditFlagForm
          flag={flag}
          onCancel={() => setEditing(false)}
          onSaved={() => {
            setEditing(false);
            reload();
          }}
        />
      ) : (
        <div className="flagdetail__view">
          <p className="flagdetail__desc">{flag.description || 'No description yet.'}</p>
          <button type="button" className="button button--quiet" onClick={() => setEditing(true)}>
            Edit
          </button>
        </div>
      )}

      <h2 className="flagdetail__historytitle">Who it reaches</h2>

      {/*
        Every environment, not the one the switcher is on. This page shows the whole flag, and a
        targeting editor that followed the switcher would hide two thirds of the answer.
      */}
      <p className="flagdetail__desc">
        A flag that is on reaches everyone unless it targets a segment. Targeting is per
        environment, so narrowing production leaves development alone.
      </p>

      {segments.status === 'failed' && (
        <div className="flaglist__failed" role="alert">
          <p>{segments.message}</p>
        </div>
      )}

      {segments.status === 'ready' &&
        environments.map((environment) => {
          const state = flag.states.find((candidate) => candidate.environment === environment.key);

          return state === undefined ? null : (
            <TargetingEditor
              key={`${environment.key}:${state.targetedSegments.join(',')}:${String(state.isEnabled)}`}
              flagKey={flag.key}
              environment={environment}
              state={state}
              segments={segments.segments}
              onChanged={reload}
            />
          );
        })}

      <h2 className="flagdetail__historytitle">Activity</h2>

      {history.status === 'loading' && (
        <p className="flaglist__note" role="status">
          Reading the activity for {flag.key}…
        </p>
      )}

      {history.status === 'failed' && (
        <div className="flaglist__failed" role="alert">
          <p>{history.message}</p>
        </div>
      )}

      {history.status === 'ready' && history.entries.length === 0 && (
        <p className="flaglist__note">No activity recorded for this flag yet.</p>
      )}

      {history.status === 'ready' && history.entries.length > 0 && (
        <ul className="historylist">
          {history.entries.map((entry, index) => (
            // Entries are an immutable server snapshot re-fetched wholesale on reload, not
            // individually updated in place, so an index key is fine here.
            <li key={index} className="historyrow">
              <p className="historyrow__summary">{summarize(entry)}</p>
              <p className="historyrow__meta">
                <span>{entry.causedByName ?? 'Unknown'}</span>
                <span className="historyrow__dot" aria-hidden="true">
                  ·
                </span>
                <span title={changedExactly(entry.occurredAt)}>{changedAgo(entry.occurredAt)}</span>
              </p>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
