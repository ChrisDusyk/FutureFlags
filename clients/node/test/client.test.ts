import { afterEach, describe, expect, it, vi } from 'vitest';

import { createFeatureFlagsClient, FeatureFlagsError } from '../src/index.js';
import { StubServer } from './stub-server.js';

// A secret key throughout: these tests are about the snapshot, the polling loop, and the
// conditional request, all of which belong to the ruleset transport. A publishable key posts its
// context instead and has no snapshot to be conditional about — see context.test.ts.
const SECRET = 'ffs_dev_1127fa3434155aab_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be559';
const BASE = 'https://flags.example.com';

function client(server: StubServer, overrides: Record<string, unknown> = {}) {
  return createFeatureFlagsClient({
    baseAddress: BASE,
    sdkKey: SECRET,
    fetch: server.fetch,
    ...overrides,
  });
}

/** The priming fetch is kicked off in the factory, so give it a turn before asserting on counts. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('reading flags', () => {
  afterEach(() => vi.useRealTimers());

  it('answers with what the server reported', async () => {
    const server = new StubServer().withFlags({ 'new-checkout': true, 'dark-mode': false }, '"v1"');
    const flags = client(server);

    expect(await flags.isEnabled('new-checkout')).toBe(true);
    expect(await flags.isEnabled('dark-mode')).toBe(false);

    flags.close();
  });

  it('does not make a request per read', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    const flags = client(server);

    for (let i = 0; i < 10; i++) {
      await flags.isEnabled('on');
    }

    // The whole point of the snapshot: a read is a map lookup, not a request.
    expect(server.callCount).toBe(1);
    flags.close();
  });

  it('is false for a flag the installation has never heard of', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    const flags = client(server);

    expect(await flags.isEnabled('never-heard-of-it')).toBe(false);
    expect(await flags.isEnabled('never-heard-of-it', true)).toBe(true);

    flags.close();
  });

  it('does not care about the key’s casing', async () => {
    const server = new StubServer().withFlags({ 'new-checkout': true }, '"v1"');
    const flags = client(server);

    expect(await flags.isEnabled('New-Checkout')).toBe(true);

    flags.close();
  });

  it('refetches once the snapshot is older than the polling interval', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"').withFlags({ on: false }, '"v2"');
    const flags = client(server, { pollingInterval: 60_000 });

    expect(await flags.isEnabled('on')).toBe(true);

    vi.setSystemTime(Date.now() + 61_000);

    expect(await flags.isEnabled('on')).toBe(false);
    flags.close();
  });
});

describe('when the server cannot be read', () => {
  it('returns the default rather than rejecting', async () => {
    const server = new StubServer().unreachable();
    const flags = client(server);

    // The behaviour this package is most likely to be judged on: a flag service being unreachable
    // must not become an outage in everything that reads it.
    expect(await flags.isEnabled('anything')).toBe(false);
    expect(await flags.isEnabled('anything', true)).toBe(true);

    flags.close();
  });

  it('keeps the last good snapshot when a refresh fails', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"').withStatus(500);
    const flags = client(server, { pollingInterval: 60_000 });

    expect(await flags.isEnabled('on')).toBe(true);

    vi.setSystemTime(Date.now() + 61_000);

    expect(await flags.isEnabled('on')).toBe(true);

    vi.useRealTimers();
    flags.close();
  });

  it('returns the default when the server hangs past the timeout', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    server.delay = 5_000;

    const flags = client(server, { timeout: 50 });

    expect(await flags.isEnabled('on')).toBe(false);
    flags.close();
  });

  /**
   * One error type out of `refresh()`, whatever went wrong. A timeout used to reject with the
   * `AbortError` `fetch` produces, so `catch (e) { if (e instanceof FeatureFlagsError) … }` held for
   * a server answering 500 and not for one answering too slowly — the failure most worth catching.
   */
  it('reports a timeout as a FeatureFlagsError, like every other failure', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    server.delay = 5_000;

    const flags = client(server, { timeout: 50 });

    await expect(flags.refresh()).rejects.toBeInstanceOf(FeatureFlagsError);
    await expect(flags.refresh()).rejects.toThrow(/did not answer within 50ms/);

    flags.close();
  });

  it('reports the failure from refresh, which asked explicitly', async () => {
    const server = new StubServer().withStatus(500);
    const flags = client(server);

    await expect(flags.refresh()).rejects.toBeInstanceOf(FeatureFlagsError);
    flags.close();
  });

  it('surfaces the server’s own words for a rejected key', async () => {
    const server = new StubServer().withStatus(401, {
      detail: 'This is a secret SDK key, and the request came from a browser.',
      code: 'SdkKey.SecretFromBrowser',
    });

    const flags = client(server);

    // The server explains this one properly. Replacing it with "unauthorized" would throw away the
    // only sentence that tells somebody what they actually did wrong.
    await expect(flags.refresh()).rejects.toThrow('came from a browser');
    flags.close();
  });

  it('falls back rather than throwing when something that is not the API answers', async () => {
    const server = new StubServer().answers_(
      () => new Response('<!doctype html><html>…', { status: 200, headers: { 'content-type': 'text/html' } }),
    );

    const flags = client(server);

    expect(await flags.isEnabled('on')).toBe(false);
    flags.close();
  });
});

describe('lifecycle', () => {
  it('collapses concurrent reads into one request', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    const flags = client(server);

    await Promise.all(Array.from({ length: 20 }, () => flags.isEnabled('on')));

    expect(server.callCount).toBe(1);
    flags.close();
  });

  it('stops asking once closed', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    const flags = client(server, { pollingInterval: 60_000 });

    await flags.isEnabled('on');
    const asked = server.callCount;

    flags.close();
    vi.setSystemTime(Date.now() + 61_000);

    // Still answering, from what it already has — it has simply stopped asking for more.
    expect(await flags.isEnabled('on')).toBe(true);
    expect(server.callCount).toBe(asked);

    vi.useRealTimers();
  });

  it('can be closed more than once', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    const flags = client(server);

    await settle();
    flags.close();

    expect(() => flags.close()).not.toThrow();
  });

  it('primes the snapshot without waiting for the first read', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    const flags = client(server);

    await settle();

    // An application that starts and immediately serves a request should not pay for the fetch on
    // that request.
    expect(server.callCount).toBe(1);
    flags.close();
  });
});
