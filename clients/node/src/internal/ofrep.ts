import type { FlagContext } from '../context.js';
import { FutureFlagsError } from '../errors.js';
import type { ResolvedOptions } from '../options.js';
import type { EvaluationErrorCode, EvaluationReason, FlagValue } from '../resolution.js';
import { authorizedHeaders, readJson, send, throwForStatus } from './http.js';
import type { Deadline } from './runtime.js';

/**
 * The OpenFeature Remote Evaluation Protocol's bulk route, as this package's browser-side provider
 * reads it.
 *
 * Separate from `remote.ts`, which reads the deprecated `POST /api/evaluation` and answers with
 * bare booleans. This one carries a value, a variant and a reason per flag, which is what an
 * OpenFeature client needs and what that route cannot give — so the provider gets real reasons
 * where `client.resolve()` on a publishable key can only say `UNKNOWN`.
 */
export const OFREP_BULK_PATH = 'ofrep/v1/evaluate/flags';

/** One flag as OFREP reports it. Either a value or an error code, never both. */
export interface OfrepFlag {
  readonly key: string;
  readonly value?: FlagValue;
  readonly variant?: string;
  readonly reason?: EvaluationReason;
  readonly errorCode?: EvaluationErrorCode;
  readonly errorDetails?: string;
}

/** What one bulk evaluation returned, and the tag that identifies it. */
export interface OfrepSnapshot {
  readonly flags: ReadonlyMap<string, OfrepFlag>;
  readonly etag: string | null;
  readonly fetchedAt: number;
}

/**
 * Evaluates every flag for one context.
 *
 * Conditional: OFREP specifies 304 on this POST, and the server's tag folds the context in as well
 * as the ruleset, so a 304 here really does mean "nothing you asked about has changed" rather than
 * "the ruleset is the same but you asked about somebody else". Null is that 304 — the caller keeps
 * what it has.
 */
export async function evaluateBulk(
  options: ResolvedOptions,
  context: FlagContext,
  current: OfrepSnapshot | null,
  attempt: Deadline,
): Promise<OfrepSnapshot | null> {
  const response = await send(
    options,
    OFREP_BULK_PATH,
    {
      method: 'POST',
      headers: {
        ...authorizedHeaders(options, current?.etag ?? null),
        'content-type': 'application/json',
      },
      body: JSON.stringify({ context: toOfrepContext(context) }),
    },
    attempt,
  );

  await throwForStatus(response, OFREP_BULK_PATH);

  if (response.status === 304) {
    return null;
  }

  const payload = await readJson(response);

  if (!isBulkResponse(payload)) {
    throw new FutureFlagsError('FutureFlags: the response was missing its flags.', response.status);
  }

  const flags = new Map<string, OfrepFlag>();

  for (const flag of payload.flags) {
    // Lowercased on the way in, matching how this client has always compared a flag key.
    flags.set(flag.key.toLowerCase(), flag);
  }

  return { flags, etag: response.headers.get('etag'), fetchedAt: Date.now() };
}

/**
 * This platform's context, flattened into OpenFeature's.
 *
 * OFREP puts custom fields alongside `targetingKey` rather than nested under `attributes`, which is
 * the one shape difference between the two protocols.
 */
function toOfrepContext(context: FlagContext): Record<string, unknown> {
  const flattened: Record<string, unknown> = {};

  if (context.key !== undefined) {
    flattened['targetingKey'] = context.key;
  }

  for (const [name, value] of Object.entries(context.attributes ?? {})) {
    flattened[name] = value;
  }

  return flattened;
}

/**
 * Checked structurally rather than trusted, on the same terms as `isRuleset`: a payload that is
 * almost right — a proxy's error page, a server too old to speak OFREP — should read as a failure
 * rather than as a set of flags that all happen to be missing.
 */
function isBulkResponse(value: unknown): value is { flags: OfrepFlag[] } {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const { flags } = value as { flags?: unknown };

  return (
    Array.isArray(flags) &&
    flags.every(
      (flag) => typeof flag === 'object' && flag !== null && typeof (flag as OfrepFlag).key === 'string',
    )
  );
}
