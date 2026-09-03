import {
  EMPTY_CONTEXT,
  fingerprintContext,
  normalizeContext,
  withDefaults,
  type FlagContext,
  type NormalizedContext,
} from './context.js';
import { SecretKeyInBrowserError } from './errors.js';
import { buildCacheKey, readFromStore, writeToStore } from './internal/cache-store.js';
import { resolveFlag, segmentsByKey, type Ruleset, type RulesetSegment } from './internal/evaluate.js';
import { asBoolean, NO_FLAG_METADATA, type FlagResolution } from './resolution.js';
import { evaluateRemotely, type AnswerSnapshot } from './internal/remote.js';
import { fetchRuleset, type RulesetSnapshot } from './internal/ruleset.js';
import { deadline, isBrowser, unref } from './internal/runtime.js';
import { resolveOptions, SECRET_KEY_PREFIX, type FutureFlagsOptions } from './options.js';

/**
 * Reads feature flags for the environment its SDK key is scoped to.
 *
 * Reads are served from memory, so `isEnabled` on a hot path is a lookup rather than a request.
 */
export interface FutureFlagsClient {
  /**
   * Whether a flag is on, for nobody in particular. A key this installation has never heard of is
   * `false` — a flag that does not exist is not one that is on — unless a default is given.
   *
   * A flag narrowed to a segment is `false` here, because a caller who has not said who is asking
   * has not described anybody the segment could contain. Pass a context to ask about a person.
   *
   * Never rejects. If the flags have never loaded, or the last refresh failed, the answer is the
   * last good one or the default: a flag service being unreachable should not take down everything
   * that reads it.
   */
  isEnabled(key: string, defaultValue?: boolean): Promise<boolean>;

  /**
   * Whether a flag is on for this person, given whatever you know about them.
   *
   * With a secret key this is evaluated in-process against the ruleset last fetched, so asking
   * about a thousand users costs a thousand lookups rather than a thousand requests. With a
   * publishable key the server evaluates it, because segment definitions have no business being in
   * a browser bundle — so a context that has not been asked about before costs one request, and is
   * then reused until it goes stale or the context changes.
   */
  isEnabled(key: string, context: FlagContext, defaultValue?: boolean): Promise<boolean>;

  /**
   * A flag's full resolution: the value, the variant it came from, why it was served, and an error
   * code when there was one.
   *
   * What `isEnabled` answers, with the reasoning attached — the distinctions a bare boolean cannot
   * make between "off in this environment", "targeted at a segment you are not in", and "no such
   * flag". It is what lets the OpenFeature provider be a thin wrapper rather than a second
   * evaluator.
   *
   * With a secret key the reason is the real one, computed here from the ruleset. With a
   * publishable key it is `UNKNOWN`: the route that answers a publishable key returns booleans and
   * no reasoning, so saying anything more definite would be inventing it. The OpenFeature web
   * provider does not have this limitation — it reads the OFREP route, which carries reasons.
   *
   * Never rejects, on the same terms as `isEnabled`.
   */
  resolve(key: string, context?: FlagContext): Promise<FlagResolution>;

  /**
   * Refetches now, rather than waiting for the polling interval. Unlike the background refresh,
   * this rejects when the fetch fails — an explicit request reports what happened.
   *
   * Every failure is a `FutureFlagsError`: a refused key, an error status, an unreadable body, a
   * server that could not be reached, or one that did not answer in time. One type to catch,
   * whichever of those it was.
   */
  refresh(): Promise<void>;

  /**
   * Stops polling and abandons any request in flight. Idempotent. The client keeps answering from
   * what it last read afterwards; it simply stops asking for anything new.
   */
  close(): void;
}

export function createFutureFlagsClient(options: FutureFlagsOptions): FutureFlagsClient {
  const resolved = resolveOptions(options);

  // Before anything else, and before any request. By the time a 401 could tell us this, the key is
  // already in a bundle somebody downloaded — the useful moment to say so is now, in development,
  // at the line that configured it.
  if (isBrowser() && resolved.sdkKey.startsWith(SECRET_KEY_PREFIX)) {
    throw new SecretKeyInBrowserError();
  }

  // The fork, and it is on the key rather than on `isBrowser()`. A publishable key used server-side
  // is a perfectly legal thing to hold, and it still cannot have the ruleset — the server refuses
  // it — so what decides the transport is what the credential is allowed to read, not where the
  // code happens to be running.
  const evaluatesLocally = resolved.sdkKey.startsWith(SECRET_KEY_PREFIX);

  const defaultContext = resolved.defaultContext
    ? normalizeContext(resolved.defaultContext)
    : null;

  let closed = false;
  let inFlight: Promise<void> | null = null;

  // Aborts whatever is in flight when close() is called, so a pending fetch cannot keep a process
  // alive or land after the caller has finished with the client.
  const lifetime = new AbortController();

  // --- the secret-key path: hold the ruleset, evaluate here -------------------------------------

  let ruleset: RulesetSnapshot | null = null;
  const cacheKey = buildCacheKey(resolved);

  // Memoized per ruleset object rather than rebuilt on every isEnabled call — this sits on the
  // hot path evaluateAll does not, since a single-flag lookup has no other reason to touch every
  // segment. Keyed by the Ruleset itself rather than tracked in a second variable: a 304 replaces
  // the RulesetSnapshot wrapper (to re-stamp fetchedAt) without replacing the Ruleset payload
  // inside it, so keying on the wrapper would rebuild on every poll that found nothing changed.
  // A WeakMap needs no invalidation — an old Ruleset is collected once nothing else holds it.
  const segmentsCache = new WeakMap<Ruleset, ReadonlyMap<string, RulesetSegment>>();

  function cachedSegmentsByKey(current: Ruleset): ReadonlyMap<string, RulesetSegment> {
    let index = segmentsCache.get(current);

    if (!index) {
      index = segmentsByKey(current);
      segmentsCache.set(current, index);
    }

    return index;
  }

  // When the store was last written to. A 304 doesn't change what's there, so it doesn't need a
  // fresh write every poll — but it does need one occasionally, or a store whose ttlSeconds is
  // shorter than "how long this process goes between real changes" would expire a perfectly
  // current entry, and a restart during that gap would find nothing. Rewriting at half the TTL
  // keeps it always at most half-expired without writing on every single poll.
  let cacheWrittenAt = 0;

  // --- the publishable-key path: hold the last answer, keyed by who it was for -------------------

  // One context, not a map of them. A browser has one signed-in user at a time, and an unbounded
  // cache of answers keyed by whoever asked is a memory leak in a long-lived process — a
  // publishable key evaluating many distinct people is the case that should be using a secret key
  // and the ruleset instead.
  let answers: AnswerSnapshot | null = null;
  let lastContext: NormalizedContext = EMPTY_CONTEXT;

  async function loadRuleset(): Promise<void> {
    const attempt = deadline(lifetime.signal, resolved.timeout);

    try {
      // A cold process has nothing yet — this is the one case a store actually changes behavior
      // for. If what it holds is still within the polling interval, it is trusted outright and the
      // origin is not asked at all; a process that starts already knowing its flags is the entire
      // point of adding a store. If it is older than that, it is still handed to fetchRuleset
      // below as the conditional-request baseline, so an unchanged answer still costs only a 304.
      if (ruleset === null && resolved.cache) {
        const cached = await readFromStore(resolved.cache, cacheKey);

        if (cached && Date.now() - cached.fetchedAt < resolved.pollingInterval) {
          ruleset = cached;

          return;
        }

        ruleset = cached;
      }

      const fetched = await fetchRuleset(resolved, ruleset, attempt);

      // Null is a 304: the answer is unchanged, so only its age moves. Without re-stamping, an
      // unchanged snapshot would look stale forever and be refetched on every read.
      ruleset = fetched ?? (ruleset ? { ...ruleset, fetchedAt: Date.now() } : null);

      if (ruleset && resolved.cache) {
        const dueForRewrite = Date.now() - cacheWrittenAt >= (resolved.cacheTtlSeconds * 1000) / 2;

        if (fetched || dueForRewrite) {
          await writeToStore(resolved.cache, cacheKey, ruleset, resolved.cacheTtlSeconds);
          cacheWrittenAt = Date.now();
        }
      }
    } finally {
      attempt.settle();
    }
  }

  async function loadAnswers(context: NormalizedContext): Promise<void> {
    const attempt = deadline(lifetime.signal, resolved.timeout);

    try {
      answers = await evaluateRemotely(resolved, context, fingerprintContext(context), attempt);
      lastContext = context;
    } finally {
      attempt.settle();
    }
  }

  /**
   * One refresh at a time. Twenty callers finding the snapshot stale at once should produce one
   * request, and the nineteen that lost should use what the winner fetched.
   */
  function refreshFor(context: NormalizedContext): Promise<void> {
    if (closed) {
      return Promise.resolve();
    }

    inFlight ??= (evaluatesLocally ? loadRuleset() : loadAnswers(context)).finally(() => {
      inFlight = null;
    });

    return inFlight;
  }

  function refresh(): Promise<void> {
    // With a publishable key there is no context-free thing to refresh, so an explicit refresh
    // reloads whoever was last asked about — which is the answer a caller is actually holding.
    return refreshFor(lastContext);
  }

  async function isEnabled(
    key: string,
    contextOrDefault?: FlagContext | boolean,
    maybeDefault?: boolean,
  ): Promise<boolean> {
    if (typeof key !== 'string') {
      throw new TypeError('FutureFlags: isEnabled needs a flag key.');
    }

    // Overloaded by shape rather than by position, so the two-argument call this package has always
    // had — isEnabled('key', true) — still means what it did.
    const context =
      typeof contextOrDefault === 'object' && contextOrDefault !== null ? contextOrDefault : null;
    const defaultValue =
      typeof contextOrDefault === 'boolean' ? contextOrDefault : (maybeDefault ?? false);

    // A reading of resolve, so the boolean surface and the resolution surface cannot drift.
    return asBoolean(await resolveWith(key, context), defaultValue);
  }

  async function resolve(key: string, context?: FlagContext): Promise<FlagResolution> {
    if (typeof key !== 'string') {
      throw new TypeError('FutureFlags: resolve needs a flag key.');
    }

    return resolveWith(key, context ?? null);
  }

  function resolveWith(key: string, context: FlagContext | null): Promise<FlagResolution> {
    const resolvedContext = withDefaults(normalizeContext(context), defaultContext);

    return evaluatesLocally ? locally(key, resolvedContext) : remotely(key, resolvedContext);
  }

  async function locally(key: string, context: NormalizedContext): Promise<FlagResolution> {
    const stale = ruleset === null || Date.now() - ruleset.fetchedAt >= resolved.pollingInterval;

    if (stale && !closed) {
      // Swallowed deliberately: the caller wants an answer, and the last good one — or their
      // default — is a better answer than a rejected promise.
      await refreshFor(context).catch(() => {});
    }

    if (!ruleset) {
      return notReady();
    }

    const wanted = key.toLowerCase();
    const flag = ruleset.ruleset.flags.find((candidate) => candidate.key.toLowerCase() === wanted);

    // A key this ruleset does not carry resolves ERROR/FLAG_NOT_FOUND, which still reads as the
    // caller's default through asBoolean. It is the one question the evaluator has no opinion on:
    // it can say whether a flag it has is on, not what a flag it has never heard of ought to mean.
    return resolveFlag(flag, cachedSegmentsByKey(ruleset.ruleset), context);
  }

  async function remotely(key: string, context: NormalizedContext): Promise<FlagResolution> {
    const fingerprint = fingerprintContext(context);
    const usable =
      answers !== null &&
      answers.fingerprint === fingerprint &&
      Date.now() - answers.fetchedAt < resolved.pollingInterval;

    if (!usable && !closed) {
      await refreshFor(context).catch(() => {});
    }

    // Only an answer computed for *this* context will do. A stale one for somebody else is worse
    // than no answer at all, so it falls through rather than being served.
    if (answers === null || answers.fingerprint !== fingerprint) {
      return notReady();
    }

    const value = answers.flags.get(key.toLowerCase());

    if (value === undefined) {
      return {
        value: false,
        variant: null,
        reason: 'ERROR',
        errorCode: 'FLAG_NOT_FOUND',
        errorMessage: 'No flag by that key exists in this environment.',
        flagMetadata: NO_FLAG_METADATA,
      };
    }

    // UNKNOWN, and deliberately so. The route a publishable key reads answers with booleans and no
    // reasoning, so naming a reason here would be inventing one — UNKNOWN is the specification's
    // word for exactly this. The OpenFeature web provider reads the OFREP route instead, which
    // carries the real reason.
    return {
      value,
      variant: null,
      reason: 'UNKNOWN',
      errorCode: null,
      errorMessage: null,
      flagMetadata: NO_FLAG_METADATA,
    };
  }

  function notReady(): FlagResolution {
    return {
      value: false,
      variant: null,
      reason: 'ERROR',
      errorCode: 'PROVIDER_NOT_READY',
      errorMessage: 'No flags have been loaded yet.',
      flagMetadata: NO_FLAG_METADATA,
    };
  }

  const timer = setInterval(() => {
    void refresh().catch(() => {
      // A polling loop that stopped on one bad response would leave the snapshot frozen at whatever
      // it last held, silently, for the life of the process.
    });
  }, resolved.pollingInterval);

  unref(timer);

  // Kick off the first load rather than waiting for the first read, so an application that starts
  // and then serves a request immediately does not pay for the fetch on that request.
  //
  // Only on the local path: a publishable-key client has nobody to ask about yet, and guessing the
  // empty context would spend a request on an answer almost no caller wants.
  //
  // The rejection is swallowed because nothing is waiting on it: an unhandled one would take a Node
  // process down over exactly the outage this client exists to survive. A caller who would rather
  // fail fast awaits `refresh()` themselves, which does report — that is the fail-fast path here,
  // and it is a plain await rather than an option that has to be discovered.
  if (evaluatesLocally) {
    void refresh().catch(() => {});
  }

  return {
    isEnabled,
    resolve,
    refresh,
    close(): void {
      if (closed) {
        return;
      }

      closed = true;
      clearInterval(timer);
      lifetime.abort();
    },
  };
}
