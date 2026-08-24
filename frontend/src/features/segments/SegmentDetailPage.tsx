import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { PageHeader } from '../../shell/PageHeader';
import { environments } from '../../shell/environment';
import { changedAgo, changedExactly } from '../flags/changed';
import {
  ApiError,
  deleteSegment,
  updateSegment,
  type SegmentDefinition,
  type SegmentDependent,
  type SegmentDetail,
  type SegmentHistoryEntry,
} from './api';
import { DefinitionEditor } from './DefinitionEditor';
import { useSegmentDetail, type SegmentHistoryState } from './useSegmentDetail';

export function SegmentDetailPage() {
  const { key = '' } = useParams();
  const { detail, history, reload } = useSegmentDetail(key);

  if (detail.status === 'loading') {
    return (
      <p className="flaglist__note" role="status">
        Reading {key}…
      </p>
    );
  }

  if (detail.status === 'missing') {
    return (
      <>
        <PageHeader eyebrow="Delivery" title={key} lede="No segment with this key exists." />
        <p className="flaglist__emptybody">
          It may have been deleted — a segment's key is never reused, so this one will not come
          back. <Link to="/segments">Back to segments</Link>.
        </p>
      </>
    );
  }

  if (detail.status === 'failed') {
    return (
      <div className="flaglist__failed" role="alert">
        <p>{detail.message}</p>
        <button type="button" className="textlink" onClick={reload}>
          Try again
        </button>
      </div>
    );
  }

  return <Loaded segment={detail.segment} history={history} onSaved={reload} />;
}

interface LoadedProps {
  segment: SegmentDetail;
  history: SegmentHistoryState;
  onSaved: () => void;
}

function Loaded({ segment, history, onSaved }: LoadedProps) {
  return (
    <>
      <PageHeader
        eyebrow="Segments"
        title={segment.name}
        lede={segment.description.length > 0 ? segment.description : `The ${segment.key} segment.`}
      />

      <p className="detail__key">
        <code>{segment.key}</code>
        <span className="seglist__changed" title={changedExactly(segment.updatedAt)}>
          changed {changedAgo(segment.updatedAt)}
        </span>
      </p>

      <EditSegmentForm segment={segment} onSaved={onSaved} />
      <Dependents targetedBy={segment.targetedBy} />
      <History history={history} />
      <DangerZone segment={segment} />
    </>
  );
}

function EditSegmentForm({ segment, onSaved }: { segment: SegmentDetail; onSaved: () => void }) {
  const [name, setName] = useState(segment.name);
  const [description, setDescription] = useState(segment.description);
  const [definition, setDefinition] = useState<SegmentDefinition>(segment.definition);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  // Re-seeds the form when a save lands and the segment is re-read, so the boxes show what the
  // server settled on rather than what was typed.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setName(segment.name);
    setDescription(segment.description);
    setDefinition(segment.definition);
  }, [segment]);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    setSaved(false);

    try {
      await updateSegment(segment.key, { name, description, definition });
      setSaved(true);
      onSaved();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : 'The segment could not be saved.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <form className="section" onSubmit={submit}>
      <h2 className="section__label">Definition</h2>

      {segment.targetedBy.length > 0 && (
        <p className="section__note" role="status">
          {segment.targetedBy.length === 1
            ? 'One flag targets this segment. '
            : `${segment.targetedBy.length} flags target this segment. `}
          Changing who is in it changes who they reach, everywhere at once.
        </p>
      )}

      <label className="field">
        <span className="field__label">Name</span>
        <input
          className="field__input"
          value={name}
          onChange={(event) => setName(event.target.value)}
          required
        />
      </label>

      <label className="field">
        <span className="field__label">Description</span>
        <input
          className="field__input"
          value={description}
          onChange={(event) => setDescription(event.target.value)}
        />
      </label>

      <DefinitionEditor definition={definition} onChange={setDefinition} disabled={saving} />

      {error !== null && (
        <p className="field__error" role="alert">
          {error}
        </p>
      )}

      <div className="dialog__actions">
        {saved && error === null && (
          <p className="section__saved" role="status">
            Saved.
          </p>
        )}
        <button type="submit" className="button" disabled={saving}>
          {saving ? 'Saving…' : 'Save segment'}
        </button>
      </div>
    </form>
  );
}

/**
 * "See who depends on it, before you edit." One entry per flag *and* environment, because a segment
 * can be holding up production while development has already moved on.
 */
function Dependents({ targetedBy }: { targetedBy: SegmentDependent[] }) {
  if (targetedBy.length === 0) {
    return (
      <section className="section">
        <h2 className="section__label">Used by</h2>
        <p className="section__note">
          No flag targets this segment yet. Until one does, editing it changes nothing for anybody.
        </p>
      </section>
    );
  }

  const byFlag = new Map<string, { name: string; environments: string[] }>();

  for (const dependent of targetedBy) {
    const existing = byFlag.get(dependent.flagKey);

    if (existing) {
      existing.environments.push(dependent.environment);
    } else {
      byFlag.set(dependent.flagKey, {
        name: dependent.flagName,
        environments: [dependent.environment],
      });
    }
  }

  return (
    <section className="section">
      <h2 className="section__label">Used by</h2>
      <ul className="dependents">
        {[...byFlag.entries()].map(([flagKey, flag]) => (
          <li key={flagKey} className="dependents__row">
            <Link className="dependents__link" to={`/flags/${encodeURIComponent(flagKey)}`}>
              {flag.name}
            </Link>
            <span className="dependents__where">
              {flag.environments.map((key) => environmentName(key)).join(', ')}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function History({ history }: { history: SegmentHistoryState }) {
  return (
    <section className="section">
      <h2 className="section__label">Activity</h2>

      {history.status === 'loading' && (
        <p className="section__note" role="status">
          Reading the history…
        </p>
      )}

      {history.status === 'failed' && (
        <p className="section__note" role="alert">
          {history.message}
        </p>
      )}

      {history.status === 'ready' && history.entries.length === 0 && (
        <p className="section__note">Nothing yet.</p>
      )}

      {history.status === 'ready' && history.entries.length > 0 && (
        <ol className="historylist">
          {history.entries.map((entry, index) => (
            <li key={index} className="historyrow">
              <p className="historyrow__summary">{summarize(entry)}</p>
              <p className="historyrow__meta">
                <span title={changedExactly(entry.occurredAt)}>{changedAgo(entry.occurredAt)}</span>
                {entry.causedByName !== null && (
                  <>
                    <span className="historyrow__dot" aria-hidden="true">
                      ·
                    </span>
                    <span>{entry.causedByName}</span>
                  </>
                )}
              </p>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function DangerZone({ segment }: { segment: SegmentDetail }) {
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);

  async function remove() {
    // A confirm(), not a custom dialog: this is irreversible in the one way that matters — the key
    // is never reissued — and the browser's own prompt is the one people actually read.
    if (!window.confirm(`Delete ${segment.name}? Its key will never be reused.`)) {
      return;
    }

    setDeleting(true);
    setError(null);

    try {
      await deleteSegment(segment.key);
      await navigate('/segments');
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : 'The segment could not be deleted.');
      setDeleting(false);
    }
  }

  return (
    <section className="section section--danger">
      <h2 className="section__label">Delete</h2>
      <p className="section__note">
        A deleted segment keeps its history and never gives its key back. Any flag still targeting
        it has to be untargeted first.
      </p>

      {error !== null && (
        <p className="field__error" role="alert">
          {error}
        </p>
      )}

      <button
        type="button"
        className="button button--danger"
        disabled={deleting}
        onClick={() => void remove()}
      >
        {deleting ? 'Deleting…' : 'Delete segment'}
      </button>
    </section>
  );
}

function environmentName(key: string): string {
  return environments.find((candidate) => candidate.key === key)?.name ?? key;
}

function summarize(entry: SegmentHistoryEntry): string {
  switch (entry.eventType) {
    case 'SegmentCreated':
      return `Created as ${entry.name ?? 'a segment'}`;
    case 'SegmentDetailsChanged':
      return `Renamed to ${entry.name ?? 'something else'}`;
    case 'SegmentDefinitionChanged':
      return describeDefinition(entry.definition);
    case 'SegmentDeleted':
      return 'Deleted';
  }
}

function describeDefinition(definition: SegmentDefinition | null): string {
  if (!definition) {
    return 'Definition changed';
  }

  const parts: string[] = [];

  if (definition.conditions.length > 0) {
    parts.push(
      `${definition.conditions.length} ${definition.conditions.length === 1 ? 'condition' : 'conditions'}`,
    );
  }

  if (definition.includedKeys.length > 0) {
    parts.push(`${definition.includedKeys.length} included`);
  }

  if (definition.excludedKeys.length > 0) {
    parts.push(`${definition.excludedKeys.length} excluded`);
  }

  return parts.length > 0 ? `Definition changed to ${parts.join(' · ')}` : 'Definition emptied';
}
