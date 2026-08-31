/**
 * A place to keep the last known-good snapshot outside this process — supplied by the consumer,
 * backed by whatever Redis client (or other store) their application already uses. Optional: a
 * client with no store behaves exactly as it does today, in-memory only, lost on restart.
 *
 * Deliberately just `get`/`set`: entries expire via their TTL rather than being explicitly
 * invalidated, the same TTL-only choice the FutureFlags server already makes for its own cache
 * (see `EvaluateFlagsHandler` on the server) — carried into the client rather than inventing a
 * second, different invalidation story here.
 *
 * A store implementation is typically a few lines wrapping a Redis client the host application
 * already has, for example with `ioredis`:
 *
 * ```typescript
 * const store: FutureFlagsCacheStore = {
 *   get: (key) => redis.get(key),
 *   set: (key, value, ttlSeconds) => redis.set(key, value, 'EX', ttlSeconds).then(() => {}),
 * };
 * ```
 */
export interface FutureFlagsCacheStore {
  /** The value previously written by `set` for this key, or `null` if there is nothing cached. */
  get(key: string): Promise<string | null>;

  /** Writes `value`, expiring it after `ttlSeconds`. */
  set(key: string, value: string, ttlSeconds: number): Promise<void>;
}
