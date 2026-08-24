import { useState } from 'react';

import type { Environment } from '../../shell/environment';
import type { SegmentSummary } from '../segments/api';
import { ApiError, setFlagTargeting, type FlagState } from './api';

interface TargetingEditorProps {
  flagKey: string;
  environment: Environment;
  state: FlagState;
  segments: SegmentSummary[];
  onChanged: () => void;
}

/**
 * Which segments a flag reaches in one environment.
 *
 * Rendered once per environment rather than following the environment switcher, because this page
 * shows the whole flag — and hiding two thirds of its targeting behind a dropdown is how a feature
 * reaches production by accident.
 */
export function TargetingEditor({
  flagKey,
  environment,
  state,
  segments,
  onChanged,
}: TargetingEditorProps) {
  const [selected, setSelected] = useState<string[]>(state.targetedSegments);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const changed =
    selected.length !== state.targetedSegments.length ||
    selected.some((key) => !state.targetedSegments.includes(key));

  async function save() {
    // The one warning worth interrupting for. Every SDK reading `GET /api/evaluation` — which is
    // every client that has not been told who is asking — sees a newly targeted flag go dark on
    // its next poll. That is correct, and it is not what somebody narrowing a flag expects.
    if (
      state.targetedSegments.length === 0 &&
      selected.length > 0 &&
      state.isEnabled &&
      !window.confirm(
        `${flagKey} is on in ${environment.name} for everyone. Narrowing it to a segment turns it ` +
          `off for anyone your app evaluates without a context — including every SDK that does not ` +
          `send one. Continue?`,
      )
    ) {
      return;
    }

    setSaving(true);
    setError(null);

    try {
      await setFlagTargeting(flagKey, environment.key, selected);
      onChanged();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : 'The targeting could not be saved.');
    } finally {
      setSaving(false);
    }
  }

  function toggle(key: string) {
    setSelected((current) =>
      current.includes(key) ? current.filter((candidate) => candidate !== key) : [...current, key],
    );
  }

  return (
    <div className="targeting">
      <div className="targeting__head">
        <span className="targeting__env" style={{ ['--env-tone' as string]: environment.tone }}>
          {environment.name}
        </span>
        <span className="targeting__summary">{summarize(state, selected.length)}</span>
      </div>

      {segments.length === 0 ? (
        <p className="section__note">
          No segments exist yet. A flag with nothing to target reaches everyone it is on for.
        </p>
      ) : (
        <ul className="targeting__options">
          {segments.map((segment) => (
            <li key={segment.id}>
              <label className="targeting__option">
                <input
                  type="checkbox"
                  checked={selected.includes(segment.key)}
                  disabled={saving}
                  onChange={() => toggle(segment.key)}
                />
                <span className="targeting__name">{segment.name}</span>
                <code className="targeting__key">{segment.key}</code>
                {segment.isEmptyDefinition && (
                  <span className="targeting__warning">nobody is in this</span>
                )}
              </label>
            </li>
          ))}
        </ul>
      )}

      {error !== null && (
        <p className="field__error" role="alert">
          {error}
        </p>
      )}

      {changed && (
        <div className="targeting__actions">
          <button
            type="button"
            className="button button--quiet"
            disabled={saving}
            onClick={() => setSelected(state.targetedSegments)}
          >
            Discard
          </button>
          <button type="button" className="button" disabled={saving} onClick={() => void save()}>
            {saving ? 'Saving…' : `Save ${environment.name} targeting`}
          </button>
        </div>
      )}
    </div>
  );
}

function summarize(state: FlagState, count: number): string {
  if (!state.isEnabled) {
    return 'Off — reaches nobody, whatever it targets';
  }

  if (count === 0) {
    return 'On for everyone';
  }

  return `On for ${count} ${count === 1 ? 'segment' : 'segments'}`;
}
