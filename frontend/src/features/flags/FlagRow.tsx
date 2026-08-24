import { useState } from 'react';
import { Link } from 'react-router-dom';

import type { Environment } from '../../shell/environment';
import { ApiError, setFlagState, type Flag } from './api';
import { changedAgo, changedExactly } from './changed';

/**
 * One flag, as one environment sees it. The state reads before the name does: a coloured edge and
 * the switch sit at opposite ends of the row, so scanning a few hundred of these tells you the
 * shape of what is live without reading a word.
 */
export function FlagRow({
  flag,
  environment,
  onReplace,
}: {
  flag: Flag;
  environment: Environment;
  onReplace: (forEnvironmentKey: string, flag: Flag) => void;
}) {
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function toggle() {
    const next = !flag.isEnabled;
    const previous = flag;

    // The environment this change is about, captured before the await. If the switcher moves
    // while the request is in flight, the answer still belongs to the environment it was asked
    // of — and onReplace drops it rather than applying it to whatever is on screen now.
    const forEnvironmentKey = environment.key;

    setPending(true);
    setError(null);

    // Show the new state straight away — the switch should feel like a switch. Everything below
    // is about making sure the row never keeps a state the server did not accept.
    onReplace(forEnvironmentKey, { ...flag, isEnabled: next });

    try {
      const settled = await setFlagState(flag.key, forEnvironmentKey, next);

      onReplace(forEnvironmentKey, {
        ...previous,
        isEnabled: settled.isEnabled,
        updatedAt: settled.updatedAt,
      });
    } catch (cause) {
      onReplace(forEnvironmentKey, previous);
      setError(
        cause instanceof ApiError
          ? cause.message
          : `The console could not change this flag in ${environment.name}.`,
      );
    } finally {
      setPending(false);
    }
  }

  const state = flag.isEnabled ? 'on' : 'off';

  return (
    <li className={`flagrow flagrow--${state}`}>
      <Link className="flagrow__body" to={`/flags/${encodeURIComponent(flag.key)}`}>
        <p className="flagrow__key">{flag.key}</p>
        <p className="flagrow__name">{flag.name}</p>
        {flag.description && <p className="flagrow__desc">{flag.description}</p>}
        <p className="flagrow__meta">
          <span className={`flagrow__state flagrow__state--${state}`}>
            {flag.isEnabled ? 'On' : 'Off'} in {environment.name}
          </span>
          {/*
            "On" and "on, for 2 segments" are different claims, and the second has to be visible
            without opening the flag — otherwise a narrowed flag reads as reaching everyone.
          */}
          {flag.isEnabled && flag.targetedSegmentCount > 0 && (
            <span className="flagrow__targeted">
              for {flag.targetedSegmentCount}{' '}
              {flag.targetedSegmentCount === 1 ? 'segment' : 'segments'}
            </span>
          )}
          <span className="flagrow__dot" aria-hidden="true">
            ·
          </span>
          <span title={changedExactly(flag.updatedAt)}>changed {changedAgo(flag.updatedAt)}</span>
        </p>

        {error && (
          <p className="flagrow__error" role="alert">
            {error}
          </p>
        )}
      </Link>

      <button
        type="button"
        className="switch"
        role="switch"
        aria-checked={flag.isEnabled}
        aria-label={`${flag.name} in ${environment.name}`}
        disabled={pending}
        onClick={() => void toggle()}
      >
        <span className="switch__track" aria-hidden="true">
          <span className="switch__thumb" />
        </span>
      </button>
    </li>
  );
}
