import type { AttributeValue, NormalizedContext } from '../context.js';
import { FeatureFlagsError } from '../errors.js';
import type { ResolvedOptions } from '../options.js';
import { authorizedHeaders, readJson, send, throwForStatus } from './http.js';
import type { Deadline } from './runtime.js';

/**
 * The answers for one context, as the server computed them.
 *
 * Keyed by a fingerprint rather than kept as a single current answer, because "the context" changes
 * when the signed-in user does and an answer computed for somebody else is worse than no answer.
 */
export interface AnswerSnapshot {
  readonly environment: string;
  readonly flags: ReadonlyMap<string, boolean>;
  /** Which version of the ruleset produced these, for a caller that wants to know when to
   * recompute anything it derived from them. */
  readonly rulesetVersion: string;
  readonly fingerprint: string;
  readonly fetchedAt: number;
}

/** Relative, so it composes with whatever path the installation is served under. */
const PATH = 'api/evaluation';

interface AnswerPayload {
  environment?: unknown;
  rulesetVersion?: unknown;
  flags?: unknown;
}

/**
 * Posts a context and takes the booleans back.
 *
 * This is the browser's half of the split. A publishable key is expected to be readable by anyone
 * who can open a bundle, so the segment definitions never leave the server — which means the
 * server has to do the evaluating. A secret key does the opposite through `fetchRuleset`, which is
 * faster and offline-tolerant and would be a disclosure here.
 */
export async function evaluateRemotely(
  options: ResolvedOptions,
  context: NormalizedContext,
  fingerprint: string,
  attempt: Deadline,
): Promise<AnswerSnapshot> {
  const attributes: Record<string, AttributeValue> = {};

  for (const [name, value] of context.attributes) {
    attributes[name] = value;
  }

  const response = await send(
    options,
    PATH,
    {
      method: 'POST',
      headers: { ...authorizedHeaders(options, null), 'content-type': 'application/json' },
      body: JSON.stringify({
        context: { key: context.key ?? undefined, attributes },
      }),
    },
    attempt,
  );

  await throwForStatus(response, PATH);

  const payload = (await readJson(response)) as AnswerPayload;

  if (typeof payload.environment !== 'string' || !isFlagMap(payload.flags)) {
    throw new FeatureFlagsError('FeatureFlags: the response was missing its flags.', response.status);
  }

  return {
    environment: payload.environment,
    // Keys are lowercase slugs on the server; lowercasing here means a caller who writes
    // 'new-Checkout' gets an answer rather than a silent default.
    flags: new Map(Object.entries(payload.flags).map(([key, on]) => [key.toLowerCase(), on])),
    rulesetVersion: typeof payload.rulesetVersion === 'string' ? payload.rulesetVersion : '',
    fingerprint,
    fetchedAt: Date.now(),
  };
}

function isFlagMap(value: unknown): value is Record<string, boolean> {
  return (
    typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    Object.values(value).every((entry) => typeof entry === 'boolean')
  );
}
