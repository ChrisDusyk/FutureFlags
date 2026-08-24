import type { FeatureFlagsCacheStore } from '../cache.js';
import type { ResolvedOptions } from '../options.js';
import { isRuleset, type RulesetSnapshot } from './ruleset.js';

/**
 * `${cacheKeyPrefix}${host}:${environment}:ruleset:v1` — host and environment, not just the
 * prefix, so two environments (or two installations) sharing one store don't overwrite each
 * other's snapshot under the same key.
 *
 * The environment segment comes from the SDK key itself (`ffs_{env}_{selector}_{secret}`), known
 * synchronously at client construction — before the first fetch has told us the environment the
 * server actually reports, which is what a cache key derived any other way would have to wait for.
 * This is namespacing only, never trusted for anything the server itself decides: an unexpected
 * segment just means a cache miss, not a wrong answer.
 */
export function buildCacheKey(resolved: ResolvedOptions): string {
  const host = new URL(resolved.baseAddress).host;
  const environment = parseEnvironment(resolved.sdkKey);

  // Versioned, because what is stored under this key used to be a map of answers and is now a
  // ruleset. Without it, an upgraded process would read an older one's entry as the wrong shape
  // for as long as cacheTtlSeconds let it survive.
  return `${resolved.cacheKeyPrefix}${host}:${environment}:ruleset:v1`;
}

function parseEnvironment(sdkKey: string): string {
  const segments = sdkKey.split('_');

  return segments.length > 1 && segments[1] ? segments[1] : 'unknown';
}

/**
 * The JSON shape written to a `FeatureFlagsCacheStore`. The ruleset is already plain JSON — it is
 * exactly what the server sent — so unlike the flag map this replaced, nothing has to be flattened
 * on the way in or rebuilt on the way out.
 */
interface StoredSnapshot {
  ruleset: unknown;
  etag: string | null;
  fetchedAt: number;
}

export function serializeSnapshot(snapshot: RulesetSnapshot): string {
  const stored: StoredSnapshot = {
    ruleset: snapshot.ruleset,
    etag: snapshot.etag,
    fetchedAt: snapshot.fetchedAt,
  };

  return JSON.stringify(stored);
}

/** Parses what `serializeSnapshot` wrote. Never throws — a store is the consumer's own Redis (or
 * whatever else), not the FeatureFlags server, so a value it cannot make sense of is treated as a
 * miss rather than a client failure. */
export function deserializeSnapshot(value: string): RulesetSnapshot | null {
  try {
    const parsed: unknown = JSON.parse(value);

    if (typeof parsed !== 'object' || parsed === null) {
      return null;
    }

    const candidate = parsed as Partial<StoredSnapshot>;

    if (
      typeof candidate.fetchedAt !== 'number' ||
      (candidate.etag !== null && typeof candidate.etag !== 'string') ||
      // Checked with the same guard the network path uses, so an entry written by an older version
      // of this package reads as a miss rather than as a ruleset in which every flag is off.
      !isRuleset(candidate.ruleset)
    ) {
      return null;
    }

    return {
      ruleset: candidate.ruleset,
      etag: candidate.etag,
      fetchedAt: candidate.fetchedAt,
    };
  } catch {
    return null;
  }
}

/** Reads the last snapshot a store holds, swallowing every failure: a blip in the consumer's own
 * Redis is not the FeatureFlags origin being unreachable, and should not read as one. */
export async function readFromStore(
  store: FeatureFlagsCacheStore,
  key: string,
): Promise<RulesetSnapshot | null> {
  try {
    const value = await store.get(key);

    return value === null ? null : deserializeSnapshot(value);
  } catch {
    return null;
  }
}

/** Writes a snapshot to a store, swallowing every failure for the same reason. */
export async function writeToStore(
  store: FeatureFlagsCacheStore,
  key: string,
  snapshot: RulesetSnapshot,
  ttlSeconds: number,
): Promise<void> {
  try {
    await store.set(key, serializeSnapshot(snapshot), ttlSeconds);
  } catch {
    // Swallowed deliberately, same as the read above.
  }
}
