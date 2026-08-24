import { describe, expect, it } from 'vitest';

import { createFeatureFlagsClient } from '../src/index.js';
import { StubServer } from './stub-server.js';

const KEY = 'ffs_dev_1127fa3434155aab_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be559';

function build(overrides: Record<string, unknown>) {
  return createFeatureFlagsClient({
    baseAddress: 'https://flags.example.com',
    sdkKey: KEY,
    fetch: new StubServer().withFlags({ on: true }, '"v1"').fetch,
    ...overrides,
  } as never);
}

/**
 * Everything here would otherwise surface as a failed fetch somewhere far from the line that caused
 * it — which, for a flag client that falls back to defaults, reads as "the flags were always off"
 * rather than as an error anyone notices.
 */
describe('options', () => {
  it('rejects a missing base address', () => {
    expect(() => build({ baseAddress: undefined })).toThrow(/baseAddress is required/);
    expect(() => build({ baseAddress: '  ' })).toThrow(/baseAddress is required/);
  });

  it('rejects a base address that is not an absolute URL', () => {
    expect(() => build({ baseAddress: 'flags.example.com' })).toThrow(/absolute URL/);
  });

  it('rejects a base address that is not http', () => {
    expect(() => build({ baseAddress: 'ftp://flags.example.com' })).toThrow(/http or https/);
  });

  it('rejects a missing key', () => {
    expect(() => build({ sdkKey: undefined })).toThrow(/sdkKey is required/);
    expect(() => build({ sdkKey: '' })).toThrow(/sdkKey is required/);
  });

  it('rejects something that is plainly not a key', () => {
    expect(() => build({ sdkKey: 'not-a-key' })).toThrow(/does not look like one/);
    expect(() => build({ sdkKey: 'eyJhbGciOiJFUzI1NiJ9.e.s' })).toThrow(/does not look like one/);
    // An unexpanded environment variable is the one people actually hit.
    expect(() => build({ sdkKey: '$FEATUREFLAGS_SDK_KEY' })).toThrow(/does not look like one/);
  });

  it('accepts both kinds of key', () => {
    for (const prefix of ['ffs', 'ffp']) {
      const flags = build({ sdkKey: KEY.replace('ffp', prefix) });
      expect(flags).toBeDefined();
      flags.close();
    }
  });

  it('rejects a non-positive interval or timeout', () => {
    expect(() => build({ pollingInterval: 0 })).toThrow(/pollingInterval/);
    expect(() => build({ pollingInterval: -1 })).toThrow(/pollingInterval/);
    expect(() => build({ timeout: 0 })).toThrow(/timeout/);
    expect(() => build({ timeout: Number.NaN })).toThrow(/timeout/);
  });

  it('rejects a non-positive cacheTtlSeconds', () => {
    expect(() => build({ cacheTtlSeconds: 0 })).toThrow(/cacheTtlSeconds/);
    expect(() => build({ cacheTtlSeconds: -1 })).toThrow(/cacheTtlSeconds/);
  });

  it('rejects a cache that does not implement get and set', () => {
    expect(() => build({ cache: {} })).toThrow(/FeatureFlagsCacheStore/);
    expect(() => build({ cache: { get: async () => null } })).toThrow(/FeatureFlagsCacheStore/);
  });

  it('is unaffected by cache options when none are given, same as before this existed', () => {
    const flags = build({});

    expect(flags).toBeDefined();
    flags.close();
  });

  it('rejects a missing options object', () => {
    expect(() => createFeatureFlagsClient(undefined as never)).toThrow(/options object/);
  });

  it('accepts a base address with a trailing slash', () => {
    const flags = build({ baseAddress: 'https://flags.example.com/' });

    expect(flags).toBeDefined();
    flags.close();
  });

  /** The SDK key is the credential, and it travels in a header. This one would only ride along in
   * whatever logs the address. */
  it('rejects a base address carrying a credential', () => {
    expect(() => build({ baseAddress: 'https://admin:hunter2@flags.example.com' })).toThrow(
      /username or password/,
    );
    expect(() => build({ baseAddress: 'https://admin@flags.example.com' })).toThrow(
      /username or password/,
    );
  });

  /**
   * Relative resolution drops both, so an address carrying either would read one way and request
   * another. Better to say so than to quietly ignore half of what was configured.
   */
  it('rejects a base address carrying a query string or a fragment', () => {
    expect(() => build({ baseAddress: 'https://flags.example.com/?tenant=acme' })).toThrow(
      /query string or fragment/,
    );
    expect(() => build({ baseAddress: 'https://flags.example.com/#top' })).toThrow(
      /query string or fragment/,
    );
  });

  it('keeps a path the installation is served under', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');

    const flags = createFeatureFlagsClient({
      baseAddress: 'https://example.com/flags',
      sdkKey: KEY,
      fetch: server.fetch,
    });

    await flags.refresh();

    expect(server.requests[0]?.url).toBe('https://example.com/flags/api/evaluation/ruleset');

    flags.close();
  });
});
