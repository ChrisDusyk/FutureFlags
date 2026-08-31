import { FutureFlagsError } from '../errors.js';
import type { ResolvedOptions } from '../options.js';
import { authorizedHeaders, readJson, send, throwForStatus } from './http.js';
import type { Ruleset } from './evaluate.js';
import type { Deadline } from './runtime.js';

/**
 * One version of the ruleset, whole. Replaced rather than mutated, so a refresh landing mid-read
 * cannot show a caller half of one version and half of another.
 */
export interface RulesetSnapshot {
  readonly ruleset: Ruleset;
  /** What to send as `If-None-Match` next time, so an unchanged poll costs a 304. */
  readonly etag: string | null;
  readonly fetchedAt: number;
}

/** Relative, so it composes with whatever path the installation is served under. */
const PATH = 'api/evaluation/ruleset';

/**
 * Fetches the ruleset, conditionally. Returns null when the server answers 304 — the caller
 * already holds that answer and should keep it.
 *
 * Only a secret key may ask: this ships every segment definition an environment uses, which is not
 * something a publishable key can be handed. The server refuses one with a 403 whose body says so.
 */
export async function fetchRuleset(
  options: ResolvedOptions,
  current: RulesetSnapshot | null,
  attempt: Deadline,
): Promise<RulesetSnapshot | null> {
  const response = await send(
    options,
    PATH,
    { headers: authorizedHeaders(options, current?.etag ?? null) },
    attempt,
  );

  if (response.status === 304 && current) {
    return null;
  }

  await throwForStatus(response, PATH);

  const payload = await readJson(response);

  if (!isRuleset(payload)) {
    throw new FutureFlagsError('FutureFlags: the response was missing its flags.', response.status);
  }

  return {
    ruleset: payload,
    etag: response.headers.get('etag'),
    fetchedAt: Date.now(),
  };
}

/**
 * Checked structurally rather than trusted. This is a network boundary, and a payload that is
 * almost right — a proxy's error page, an older server — should read as a failure rather than as a
 * ruleset in which every flag happens to be off.
 *
 * Every array is checked down to its elements, not just its own type. `targetedSegments` holding a
 * number instead of a key, for instance, would otherwise pass this guard and then fail silently at
 * evaluation time — `segments.get(42)` finds nothing and reads exactly like a legitimately retired
 * segment, which is a real answer to a different question. A malformed payload should fail here,
 * where it can be reported, rather than downstream where it looks like ordinary "not targeted".
 */
export function isRuleset(value: unknown): value is Ruleset {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<Ruleset>;

  return (
    typeof candidate.environment === 'string' &&
    Array.isArray(candidate.flags) &&
    Array.isArray(candidate.segments) &&
    candidate.flags.every(isRulesetFlag) &&
    candidate.segments.every(isRulesetSegment)
  );
}

function isRulesetFlag(value: unknown): value is Ruleset['flags'][number] {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const flag = value as Partial<Ruleset['flags'][number]>;

  return (
    typeof flag.key === 'string' &&
    typeof flag.isEnabled === 'boolean' &&
    Array.isArray(flag.targetedSegments) &&
    flag.targetedSegments.every((segmentKey) => typeof segmentKey === 'string')
  );
}

function isRulesetSegment(value: unknown): value is Ruleset['segments'][number] {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const segment = value as Partial<Ruleset['segments'][number]>;

  return (
    typeof segment.key === 'string' &&
    Array.isArray(segment.included) &&
    segment.included.every((key) => typeof key === 'string') &&
    Array.isArray(segment.excluded) &&
    segment.excluded.every((key) => typeof key === 'string') &&
    Array.isArray(segment.conditions) &&
    segment.conditions.every(isRulesetCondition)
  );
}

function isRulesetCondition(value: unknown): value is Ruleset['segments'][number]['conditions'][number] {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const condition = value as Partial<Ruleset['segments'][number]['conditions'][number]>;

  return (
    typeof condition.attribute === 'string' &&
    typeof condition.operator === 'string' &&
    Array.isArray(condition.values) &&
    condition.values.every(
      (candidate) =>
        typeof candidate === 'string' ||
        typeof candidate === 'number' ||
        typeof candidate === 'boolean',
    )
  );
}
