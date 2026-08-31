import type { FutureFlagsCacheStore } from '../src/index.js';

/** An in-memory stand-in for a consumer's own Redis (or whatever else backs a
 * `FutureFlagsCacheStore`), with hooks for the failure modes a real store can have. */
export class FakeCacheStore implements FutureFlagsCacheStore {
  private readonly values = new Map<string, string>();

  getCalls = 0;
  setCalls = 0;
  lastTtlSeconds: number | null = null;

  /** Set to make every get() reject, standing in for the consumer's store having its own blip. */
  failGet = false;

  /** Set to make every set() reject. */
  failSet = false;

  seed(key: string, value: string): this {
    this.values.set(key, value);

    return this;
  }

  has(key: string): boolean {
    return this.values.has(key);
  }

  async get(key: string): Promise<string | null> {
    this.getCalls++;

    if (this.failGet) {
      throw new Error('FakeCacheStore: get() was told to fail.');
    }

    return this.values.get(key) ?? null;
  }

  async set(key: string, value: string, ttlSeconds: number): Promise<void> {
    this.setCalls++;
    this.lastTtlSeconds = ttlSeconds;

    if (this.failSet) {
      throw new Error('FakeCacheStore: set() was told to fail.');
    }

    this.values.set(key, value);
  }
}
