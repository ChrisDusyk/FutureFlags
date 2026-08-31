import { useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  EnvironmentContext,
  environments,
  type EnvironmentId,
  type EnvironmentSelection,
} from './environment';

const STORAGE_KEY = 'futureflags.console.environment';

/** Development is the default on purpose — the safest place to land. */
const FALLBACK: EnvironmentId = 'development';

/*
 * Storage can be unavailable outright — blocked by policy, disabled, or an
 * embedded context with no access. Remembering the environment is a nicety;
 * taking the console down with it is not, so both paths swallow the failure.
 */

function readStoredId(): EnvironmentId {
  let stored: string | null = null;

  try {
    stored = localStorage.getItem(STORAGE_KEY);
  } catch {
    return FALLBACK;
  }

  return environments.some((environment) => environment.id === stored)
    ? (stored as EnvironmentId)
    : FALLBACK;
}

function storeId(id: EnvironmentId): void {
  try {
    localStorage.setItem(STORAGE_KEY, id);
  } catch {
    // The choice still holds for this session, it just won't outlive the tab.
  }
}

export function EnvironmentProvider({ children }: { children: ReactNode }) {
  const [id, setId] = useState<EnvironmentId>(readStoredId);

  useEffect(() => {
    storeId(id);
  }, [id]);

  const selection = useMemo<EnvironmentSelection>(
    () => ({
      environment: environments.find((environment) => environment.id === id) ?? environments[0],
      select: setId,
    }),
    [id],
  );

  return <EnvironmentContext.Provider value={selection}>{children}</EnvironmentContext.Provider>;
}
