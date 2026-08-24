import type { FeatureFlagsCacheStore } from './cache.js';
import type { FlagContext } from './context.js';

/**
 * How to reach a FeatureFlags installation.
 *
 * There is deliberately no environment here. An SDK key is issued for one environment and carries
 * it, so the server decides which flags this client sees — one thing to configure, and no way for
 * it to drift from what the console shows.
 */
export interface FeatureFlagsOptions {
  /**
   * The origin the console is on — `https://flags.example.com`. The same value the installation
   * was deployed with as `FEATUREFLAGS_ORIGIN`.
   */
  baseAddress: string;

  /**
   * A key issued under Organization → Environments. It is shown once, when it is issued.
   *
   * In a browser this has to be a **publishable** key (`ffp_`). A secret key (`ffs_`) is refused
   * by the server when the request comes from a browser, and this client refuses to start with one
   * so the mistake surfaces where it was made rather than as a failed fetch later.
   */
  sdkKey: string;

  /**
   * How stale an answer may get before it is refetched. Defaults to 30 seconds.
   *
   * This is the upper bound on how long a toggle takes to reach this process, and the lower bound
   * on how often it asks — a poll that finds nothing changed costs a 304 with no body, so it can
   * be shorter than it looks.
   */
  pollingInterval?: number;

  /** How long a single refresh may take before it is abandoned, in milliseconds. Defaults to 10 seconds. */
  timeout?: number;

  /**
   * The `fetch` to use. Defaults to the global one, which both Node 20+ and browsers have. Present
   * for tests and for anyone who has to route through a proxy agent.
   */
  fetch?: typeof globalThis.fetch;

  /**
   * Where to keep the last known-good snapshot outside this process, so a fresh process — or one
   * that just restarted — does not start from nothing while it waits for its first fetch to land.
   * Optional, and additive: omitted, this client behaves exactly as it does today, in-memory only,
   * lost on restart. There is no default implementation because there is no Redis client this
   * package could import without breaking browser bundles for everyone who never touches this
   * option — see `FeatureFlagsCacheStore`.
   */
  cache?: FeatureFlagsCacheStore;

  /**
   * How long a snapshot written to `cache` may still be served after a real outage, in seconds.
   * Only meaningful with `cache` set. This is deliberately a different number from
   * `pollingInterval`, and a much larger one — it is the backstop for an outage, not the normal
   * freshness bound. Defaults to 86400 (24 hours).
   */
  cacheTtlSeconds?: number;

  /**
   * Traits every evaluation should carry, whether or not the call site mentions them — the region
   * this deployment runs in, its tier, the build it is on.
   *
   * A per-call context is laid over this one, so anything named at the call site wins. Omitted, the
   * default, means every evaluation is described entirely by its caller.
   *
   * This is for facts about the *process*, not about a person. A default context carrying a user
   * would answer for that user on every call that forgot to say otherwise, which is the least
   * obvious way to get a wrong answer.
   */
  defaultContext?: FlagContext;

  /**
   * Prefixed onto the key this client uses in `cache`, so it cannot collide with unrelated keys in
   * a store the host application also uses for its own caching, or with another environment's
   * client sharing the same store. Defaults to `'featureflags:'`.
   */
  cacheKeyPrefix?: string;
}

export interface ResolvedOptions
  extends Required<Omit<FeatureFlagsOptions, 'fetch' | 'cache' | 'defaultContext'>> {
  fetch: typeof globalThis.fetch;
  cache: FeatureFlagsCacheStore | null;
  defaultContext: FlagContext | null;
}

export const SECRET_KEY_PREFIX = 'ffs_';
export const PUBLISHABLE_KEY_PREFIX = 'ffp_';

const DEFAULTS = {
  pollingInterval: 30_000,
  timeout: 10_000,
  cacheTtlSeconds: 86_400,
  cacheKeyPrefix: 'featureflags:',
} as const;

/**
 * Fills in the defaults and rejects what could only fail later.
 *
 * Everything checked here is something that would otherwise surface as a 401 or a failed fetch
 * somewhere far from the line that caused it — which for a flag client means "the flags were
 * always off" rather than an error anyone notices.
 */
export function resolveOptions(options: FeatureFlagsOptions): ResolvedOptions {
  if (!options || typeof options !== 'object') {
    throw new TypeError('createFeatureFlagsClient needs an options object.');
  }

  const baseAddress = requireOrigin(options.baseAddress);
  const sdkKey = requireSdkKey(options.sdkKey);

  const pollingInterval = options.pollingInterval ?? DEFAULTS.pollingInterval;
  const timeout = options.timeout ?? DEFAULTS.timeout;

  if (!Number.isFinite(pollingInterval) || pollingInterval <= 0) {
    throw new TypeError('FeatureFlags: pollingInterval must be a positive number of milliseconds.');
  }

  if (!Number.isFinite(timeout) || timeout <= 0) {
    throw new TypeError('FeatureFlags: timeout must be a positive number of milliseconds.');
  }

  const cacheTtlSeconds = options.cacheTtlSeconds ?? DEFAULTS.cacheTtlSeconds;

  if (!Number.isFinite(cacheTtlSeconds) || cacheTtlSeconds <= 0) {
    throw new TypeError('FeatureFlags: cacheTtlSeconds must be a positive number of seconds.');
  }

  const cacheKeyPrefix = options.cacheKeyPrefix ?? DEFAULTS.cacheKeyPrefix;

  if (typeof cacheKeyPrefix !== 'string') {
    throw new TypeError('FeatureFlags: cacheKeyPrefix must be a string.');
  }

  const defaultContext = options.defaultContext ?? null;

  if (defaultContext !== null && (typeof defaultContext !== 'object' || Array.isArray(defaultContext))) {
    throw new TypeError(
      'FeatureFlags: defaultContext must be an object with an optional key and attributes.',
    );
  }

  const cache = options.cache ?? null;

  // Checked structurally rather than trusting the type: this is the one option a caller is most
  // likely to pass something almost-right for (an actual Redis client, not the small interface
  // wrapping one), and failing here beats failing inside the first background refresh instead.
  if (cache !== null && (typeof cache.get !== 'function' || typeof cache.set !== 'function')) {
    throw new TypeError(
      'FeatureFlags: cache must implement get(key) and set(key, value, ttlSeconds) — see FeatureFlagsCacheStore.',
    );
  }

  const resolvedFetch = options.fetch ?? globalThis.fetch;

  if (typeof resolvedFetch !== 'function') {
    throw new TypeError(
      'FeatureFlags: no fetch is available. Node 20 or later has one built in; otherwise pass one as options.fetch.',
    );
  }

  return {
    baseAddress,
    sdkKey,
    pollingInterval,
    timeout,
    cacheTtlSeconds,
    cacheKeyPrefix,
    cache,
    defaultContext,
    // Bound, because an unbound global fetch throws "Illegal invocation" in a browser.
    fetch: resolvedFetch.bind(globalThis),
  };
}

function requireOrigin(value: string): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new TypeError(
      'FeatureFlags: baseAddress is required. It is the origin the console is on, for example https://flags.example.com.',
    );
  }

  let url: URL;

  try {
    url = new URL(value);
  } catch {
    throw new TypeError(
      `FeatureFlags: baseAddress must be an absolute URL including the scheme — got ${JSON.stringify(value)}.`,
    );
  }

  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new TypeError(
      `FeatureFlags: baseAddress must be http or https — got ${JSON.stringify(url.protocol)}.`,
    );
  }

  // A credential in the URL is not one this client can use — the SDK key is the credential, and it
  // travels in a header. What it would do instead is ride along in anything that logs the address.
  if (url.username.length > 0 || url.password.length > 0) {
    throw new TypeError(
      'FeatureFlags: baseAddress must not carry a username or password. The SDK key is the credential.',
    );
  }

  // A path is kept, because an installation may be served under one. A query or a fragment is not:
  // relative resolution drops both, so keeping them would mean an address that reads one way and
  // requests another. Refused for the same reason the server refuses them in FEATUREFLAGS_ORIGIN.
  if (url.search.length > 0 || url.hash.length > 0) {
    throw new TypeError(
      'FeatureFlags: baseAddress must be an address, with no query string or fragment.',
    );
  }

  // A trailing slash, so URL composition keeps any path the installation is served under instead
  // of dropping its last segment.
  const address = `${url.origin}${url.pathname}`;

  return address.endsWith('/') ? address : `${address}/`;
}

function requireSdkKey(value: string): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new TypeError(
      'FeatureFlags: sdkKey is required. Issue one in the console under Organization → Environments.',
    );
  }

  const key = value.trim();

  // Matched loosely on purpose: this catches a value that is obviously not a key — an unexpanded
  // environment variable, a JWT pasted by mistake. Whether the key is *valid* is the server's to
  // say, and only it can.
  if (!key.startsWith(SECRET_KEY_PREFIX) && !key.startsWith(PUBLISHABLE_KEY_PREFIX)) {
    throw new TypeError(
      `FeatureFlags: sdkKey does not look like one — it should begin with ${SECRET_KEY_PREFIX} or ${PUBLISHABLE_KEY_PREFIX}.`,
    );
  }

  return key;
}
