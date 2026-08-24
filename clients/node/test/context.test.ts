import { describe, expect, it } from 'vitest';

import { createFeatureFlagsClient, type FlagContext } from '../src/index.js';
import { StubServer } from './stub-server.js';

const SECRET = 'ffs_dev_1127fa3434155aab_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be559';
const PUBLISHABLE = 'ffp_dev_b182276126b759aa_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be559';
const BASE = 'https://flags.example.com';

const TARGETED = {
  environment: 'dev',
  flags: [
    { key: 'new-checkout', isEnabled: true, targetedSegments: ['pro-users'] },
    { key: 'dark-mode', isEnabled: true, targetedSegments: [] },
  ],
  segments: [
    {
      key: 'pro-users',
      included: ['user-17'],
      excluded: ['user-99'],
      conditions: [{ attribute: 'plan', operator: 'equals', values: ['pro'] }],
    },
  ],
};

function localClient(defaultContext?: FlagContext) {
  const server = new StubServer().withRuleset(TARGETED, '"v1"');

  return {
    server,
    flags: createFeatureFlagsClient({
      baseAddress: BASE,
      sdkKey: SECRET,
      fetch: server.fetch,
      defaultContext,
    }),
  };
}

describe('evaluating for a person with a secret key', () => {
  it('answers true for a matching context', async () => {
    const { flags } = localClient();

    expect(await flags.isEnabled('new-checkout', { key: 'u1', attributes: { plan: 'pro' } })).toBe(
      true,
    );
    flags.close();
  });

  it('answers false for a context that matches nothing', async () => {
    const { flags } = localClient();

    expect(await flags.isEnabled('new-checkout', { key: 'u1', attributes: { plan: 'free' } })).toBe(
      false,
    );
    flags.close();
  });

  it('reads a targeted flag as off when nobody was described', async () => {
    // The compatible reading, and what keeps the no-context call meaning something: a caller who
    // has not said who is asking has not described anybody a segment could contain.
    const { flags } = localClient();

    expect(await flags.isEnabled('new-checkout')).toBe(false);
    flags.close();
  });

  it('still answers an untargeted flag normally with no context', async () => {
    const { flags } = localClient();

    expect(await flags.isEnabled('dark-mode')).toBe(true);
    flags.close();
  });

  it('matches on an included key with no attributes at all', async () => {
    const { flags } = localClient();

    expect(await flags.isEnabled('new-checkout', { key: 'user-17' })).toBe(true);
    flags.close();
  });

  it('lets an excluded key beat a matching attribute', async () => {
    const { flags } = localClient();

    expect(
      await flags.isEnabled('new-checkout', { key: 'user-99', attributes: { plan: 'pro' } }),
    ).toBe(false);
    flags.close();
  });

  it('does not coerce between types', async () => {
    const { flags } = localClient();

    // The condition is written against the string 'pro'; a number is a different value, not a
    // value to render and compare.
    expect(await flags.isEnabled('new-checkout', { key: 'u1', attributes: { plan: 2 } })).toBe(
      false,
    );
    flags.close();
  });

  it('takes attributes from defaultContext', async () => {
    const { flags } = localClient({ attributes: { plan: 'pro' } });

    expect(await flags.isEnabled('new-checkout', { key: 'u1' })).toBe(true);
    flags.close();
  });

  it('lets a per-call attribute beat the default', async () => {
    const { flags } = localClient({ attributes: { plan: 'pro' } });

    expect(await flags.isEnabled('new-checkout', { key: 'u1', attributes: { plan: 'free' } })).toBe(
      false,
    );
    flags.close();
  });

  it('evaluates many people without asking the server again', async () => {
    // The property that makes per-user evaluation affordable at all.
    const { flags, server } = localClient();

    for (let i = 0; i < 10; i += 1) {
      await flags.isEnabled('new-checkout', { key: `user-${i}`, attributes: { plan: 'pro' } });
    }

    expect(server.callCount).toBe(1);
    flags.close();
  });

  it('still honours the old two-argument call with a boolean default', async () => {
    const { flags } = localClient();

    expect(await flags.isEnabled('never-heard-of-it', true)).toBe(true);
    expect(await flags.isEnabled('never-heard-of-it', false)).toBe(false);
    flags.close();
  });

  it('returns the default for an unknown key even with a context', async () => {
    const { flags } = localClient();

    expect(await flags.isEnabled('never-heard-of-it', { key: 'user-17' }, true)).toBe(true);
    flags.close();
  });
});

describe('evaluating for a person with a publishable key', () => {
  it('posts the context and takes the booleans back', async () => {
    const server = new StubServer().withAnswers({ 'new-checkout': true });
    const flags = createFeatureFlagsClient({ baseAddress: BASE, sdkKey: PUBLISHABLE, fetch: server.fetch });

    expect(await flags.isEnabled('new-checkout', { key: 'u1', attributes: { plan: 'pro' } })).toBe(
      true,
    );

    const request = server.requests[0]!;

    // The fork is on the key, not on whether this is a browser: a publishable key cannot have the
    // ruleset wherever it runs, because the server will not give it one.
    expect(request.method).toBe('POST');
    expect(request.url).toBe(`${BASE}/api/evaluation`);
    expect(JSON.parse(request.body!)).toEqual({
      context: { key: 'u1', attributes: { plan: 'pro' } },
    });

    flags.close();
  });

  it('does not ask again for the same context', async () => {
    const server = new StubServer().withAnswers({ 'new-checkout': true });
    const flags = createFeatureFlagsClient({ baseAddress: BASE, sdkKey: PUBLISHABLE, fetch: server.fetch });

    await flags.isEnabled('new-checkout', { key: 'u1' });
    await flags.isEnabled('new-checkout', { key: 'u1' });
    await flags.isEnabled('dark-mode', { key: 'u1' });

    expect(server.callCount).toBe(1);
    flags.close();
  });

  it('asks again when the context changes', async () => {
    const server = new StubServer()
      .withAnswers({ 'new-checkout': true })
      .withAnswers({ 'new-checkout': false });

    const flags = createFeatureFlagsClient({ baseAddress: BASE, sdkKey: PUBLISHABLE, fetch: server.fetch });

    expect(await flags.isEnabled('new-checkout', { key: 'u1' })).toBe(true);
    // A different person is a different question, and the answer held is not an answer about them.
    expect(await flags.isEnabled('new-checkout', { key: 'u2' })).toBe(false);
    expect(server.callCount).toBe(2);

    flags.close();
  });

  it('never serves one person’s answers to another', async () => {
    const server = new StubServer().withAnswers({ 'new-checkout': true }).unreachable();
    const flags = createFeatureFlagsClient({ baseAddress: BASE, sdkKey: PUBLISHABLE, fetch: server.fetch });

    expect(await flags.isEnabled('new-checkout', { key: 'u1' })).toBe(true);

    // The server is unreachable for the second person, so there is no answer about them. Falling
    // back to the first person's is the one failure mode this path must not have.
    expect(await flags.isEnabled('new-checkout', { key: 'u2' })).toBe(false);
    expect(await flags.isEnabled('new-checkout', { key: 'u2' }, true)).toBe(true);

    flags.close();
  });

  it('does not spend a request before anybody has been described', async () => {
    const server = new StubServer().withAnswers({ 'new-checkout': true });

    createFeatureFlagsClient({ baseAddress: BASE, sdkKey: PUBLISHABLE, fetch: server.fetch });

    // The local path primes itself at construction because it has something context-free to fetch.
    // This one does not, and guessing the empty context would buy an answer almost nobody wants.
    await Promise.resolve();
    expect(server.callCount).toBe(0);
  });
});
