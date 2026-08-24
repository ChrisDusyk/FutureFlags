import { FeatureFlagsError } from '../errors.js';
import type { ResolvedOptions } from '../options.js';
import type { Deadline } from './runtime.js';

/**
 * What both transports share: how a request is addressed and credentialed, and how a failure is
 * turned into something a caller can act on.
 *
 * Lifted out when a second transport arrived. Two copies of "is this a timeout, a close, or an
 * outage" is two chances to get the distinction wrong, and the distinction is what decides whether
 * a caller sees an error or their last good answer.
 */
export function authorizedHeaders(options: ResolvedOptions, etag: string | null): Record<string, string> {
  const headers: Record<string, string> = {
    accept: 'application/json',
    authorization: `Bearer ${options.sdkKey}`,
  };

  if (etag) {
    // Echoed back exactly as it arrived. Parsing and rebuilding it is a chance to change it, and a
    // changed validator never matches.
    headers['if-none-match'] = etag;
  }

  return headers;
}

/**
 * Sends, translating the ways a request can fail to arrive.
 *
 * A timeout is reported as a `FeatureFlagsError` because it is a way of not reaching the server —
 * what `fetch` rejects an abort with is an `AbortError`, a different type from every other failure
 * here, which would otherwise make "catch a FeatureFlagsError" true of a slow server but not a
 * stopped one. The other abort is `close()`, which is a cancellation the caller asked for, and
 * wrapping that would dress up a request nobody is waiting for as an outage.
 */
export async function send(
  options: ResolvedOptions,
  path: string,
  init: RequestInit,
  attempt: Deadline,
): Promise<Response> {
  try {
    return await options.fetch(new URL(path, options.baseAddress), {
      ...init,
      signal: attempt.signal,
    });
  } catch (cause) {
    if (attempt.expired) {
      throw new FeatureFlagsError(
        `FeatureFlags: the server did not answer within ${options.timeout}ms.`,
        0,
        { cause },
      );
    }

    if (attempt.signal.aborted) {
      throw cause;
    }

    throw new FeatureFlagsError('FeatureFlags: the server could not be reached.', 0, { cause });
  }
}

/** Throws for any status that is not an answer. Returns for 2xx and for 304. */
export async function throwForStatus(response: Response, path: string): Promise<void> {
  if (response.status === 401 || response.status === 403) {
    throw new FeatureFlagsError(await rejectionMessage(response), response.status);
  }

  if (!response.ok && response.status !== 304) {
    throw new FeatureFlagsError(
      `FeatureFlags: the server answered ${response.status} for /${path}.`,
      response.status,
    );
  }
}

/** Reads a JSON body, saying something useful when it is not JSON at all. */
export async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch (cause) {
    throw new FeatureFlagsError(
      'FeatureFlags: the response could not be read. This usually means something other than the ' +
        'API answered — a proxy, or a login page.',
      response.status,
      { cause },
    );
  }
}

/**
 * The server explains a refused credential in ProblemDetails, and the explanation is worth
 * surfacing verbatim — "this is a publishable key and the ruleset is not published to browsers" is
 * a great deal more useful than the status code that carried it, and points at the fix.
 */
async function rejectionMessage(response: Response): Promise<string> {
  try {
    const problem: unknown = await response.json();

    if (typeof problem === 'object' && problem !== null) {
      const { detail } = problem as { detail?: unknown };

      if (typeof detail === 'string' && detail.length > 0) {
        return `FeatureFlags: ${detail}`;
      }
    }
  } catch {
    // Not JSON. The status still says something.
  }

  return 'FeatureFlags: the server rejected this SDK key. It may have been revoked, or it may belong to a different installation.';
}
