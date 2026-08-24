import { afterEach, describe, expect, it, vi } from 'vitest';

import { createFeatureFlagsClient } from '../src/index.js';
import { StubServer } from './stub-server.js';

// A secret key throughout: these tests are about the snapshot, the polling loop, and the
// conditional request, all of which belong to the ruleset transport. A publishable key posts its
// context instead and has no snapshot to be conditional about — see remote-transport.test.ts.
const KEY = 'ffs_dev_1127fa3434155aab_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be559';

function client(server: StubServer, baseAddress = 'https://flags.example.com') {
  return createFeatureFlagsClient({
    baseAddress,
    sdkKey: KEY,
    fetch: server.fetch,
    pollingInterval: 60_000,
  });
}

const age = (ms: number) => vi.setSystemTime(Date.now() + ms);

describe('the request', () => {
  afterEach(() => vi.useRealTimers());

  it('carries the key as a bearer token and asks for JSON', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    const flags = client(server);

    await flags.isEnabled('on');

    const request = server.requests[0]!;
    expect(request.headers.get('authorization')).toBe(`Bearer ${KEY}`);
    expect(request.headers.get('accept')).toBe('application/json');
    expect(request.url).toBe('https://flags.example.com/api/evaluation/ruleset');

    flags.close();
  });

  it('keeps a path the installation is served under', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    const flags = client(server, 'https://example.com/flags');

    await flags.isEnabled('on');

    // Without the trailing slash the options layer adds, URL composition drops "flags" and the
    // request quietly goes somewhere else.
    expect(server.requests[0]!.url).toBe('https://example.com/flags/api/evaluation/ruleset');

    flags.close();
  });

  it('sends no If-None-Match the first time', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"');
    const flags = client(server);

    await flags.isEnabled('on');

    expect(server.requests[0]!.headers.has('if-none-match')).toBe(false);
    flags.close();
  });

  it('sends back the tag it was given', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"').notModified();
    const flags = client(server);

    await flags.isEnabled('on');
    age(61_000);
    await flags.isEnabled('on');

    expect(server.requests[1]!.headers.get('if-none-match')).toBe('"v1"');
    flags.close();
  });
});

describe('a 304', () => {
  afterEach(() => vi.useRealTimers());

  it('keeps the previous answer', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"').notModified();
    const flags = client(server);

    await flags.isEnabled('on');
    age(61_000);

    expect(await flags.isEnabled('on')).toBe(true);
    flags.close();
  });

  it('resets the snapshot’s age', async () => {
    const server = new StubServer().withFlags({ on: true }, '"v1"').notModified();
    const flags = client(server);

    await flags.isEnabled('on');
    age(61_000);
    await flags.isEnabled('on');

    const afterTheRefresh = server.callCount;

    for (let i = 0; i < 5; i++) {
      await flags.isEnabled('on');
    }

    // Without re-stamping, an unchanged answer would look stale forever and be refetched on every
    // single read — which is the opposite of what a 304 is for.
    expect(server.callCount).toBe(afterTheRefresh);
    flags.close();
  });
});
