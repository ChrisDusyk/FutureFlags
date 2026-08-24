import { afterEach, describe, expect, it, vi } from 'vitest';

import { createFeatureFlagsClient, SecretKeyInBrowserError } from '../src/index.js';
import { StubServer } from './stub-server.js';

const SECRET = 'ffs_dev_1127fa3434155aab_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be559';
const PUBLISHABLE = 'ffp_dev_b182276126b759aa_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be559';

function inABrowser() {
  vi.stubGlobal('window', {});
  vi.stubGlobal('document', {});
}

function build(sdkKey: string) {
  // Answers whichever transport is asked for, because which one a client uses is decided by its
  // key — a secret key pulls the ruleset, a publishable one posts its context — and that fork is
  // what these tests are ultimately about.
  const fetch: typeof globalThis.fetch = (input, init) => {
    const body = String(input).endsWith('/ruleset')
      ? {
          environment: 'dev',
          flags: [{ key: 'on', isEnabled: true, targetedSegments: [] }],
          segments: [],
        }
      : { environment: 'dev', rulesetVersion: '"r1"', flags: { on: true } };

    void init;

    return Promise.resolve(
      new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'content-type': 'application/json', etag: '"v1"' },
      }),
    );
  };

  return createFeatureFlagsClient({
    baseAddress: 'https://flags.example.com',
    sdkKey,
    fetch,
  });
}

/**
 * The client's half of the rule the server enforces. The server is the authority — it refuses a
 * secret key on any request carrying an Origin header — but a 401 arrives long after the mistake,
 * and by then the key is in a bundle somebody downloaded. This is the same rule, said at the line
 * that configured it.
 */
describe('a secret key in a browser', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('is refused at construction', () => {
    inABrowser();

    expect(() => build(SECRET)).toThrow(SecretKeyInBrowserError);
  });

  it('says what to do about it', () => {
    inABrowser();

    try {
      build(SECRET);
      expect.unreachable('should have thrown');
    } catch (error) {
      const message = (error as Error).message;

      // Two things a developer needs: that the key they are holding is now public, and the exact
      // thing to go and do instead.
      expect(message).toContain('revoke');
      expect(message).toContain('ffp_');
    }
  });

  it('does not make a request first', () => {
    inABrowser();

    const server = new StubServer().withFlags({ on: true }, '"v1"');

    expect(() =>
      createFeatureFlagsClient({
        baseAddress: 'https://flags.example.com',
        sdkKey: SECRET,
        fetch: server.fetch,
      }),
    ).toThrow(SecretKeyInBrowserError);

    // The point of catching it here is that the key never leaves the page.
    expect(server.callCount).toBe(0);
  });
});

describe('a publishable key in a browser', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('is fine', async () => {
    inABrowser();

    const flags = build(PUBLISHABLE);

    expect(await flags.isEnabled('on')).toBe(true);
    flags.close();
  });
});

describe('on a server', () => {
  it('accepts a secret key', async () => {
    const flags = build(SECRET);

    expect(await flags.isEnabled('on')).toBe(true);
    flags.close();
  });

  it('accepts a publishable key too', async () => {
    // Nothing stops a server from holding a publishable one. It reads the same flags; it is only
    // less careful about who else could.
    const flags = build(PUBLISHABLE);

    expect(await flags.isEnabled('on')).toBe(true);
    flags.close();
  });

  it('treats a server-side render as a server', async () => {
    // `window` without `document` is not a browser — some server runtimes define one. Both are
    // required, which is what keeps SSR from tripping the guard on its server pass.
    vi.stubGlobal('window', {});

    const flags = build(SECRET);

    expect(await flags.isEnabled('on')).toBe(true);
    flags.close();
    vi.unstubAllGlobals();
  });
});
