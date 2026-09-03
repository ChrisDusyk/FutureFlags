import { describe, expect, it } from 'vitest';

import { FutureFlagsServerProvider } from '../src/openfeature/server.js';
import { FutureFlagsWebProvider } from '../src/openfeature/web.js';
import { StubServer } from './stub-server.js';

const SECRET_KEY =
  'ffs_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10';
const PUBLISHABLE_KEY =
  'ffp_dev_f992c8928754087a_7f097037aa14d671f4317df877989f05f5309c1323ecb24dab4be5597f40db10';

const BASE = 'https://flags.example.com';

describe('the server-side OpenFeature provider', () => {
  function provider(server: StubServer) {
    return new FutureFlagsServerProvider({
      baseAddress: BASE,
      sdkKey: SECRET_KEY,
      fetch: server.fetch,
    });
  }

  it('refuses a publishable key at construction rather than at the first evaluation', () => {
    // The server would answer 403 to the ruleset request and the provider would come up looking
    // healthy while resolving nothing. The useful moment to say so is the line that configured it.
    expect(
      () => new FutureFlagsServerProvider({ baseAddress: BASE, sdkKey: PUBLISHABLE_KEY }),
    ).toThrow(/secret/i);
  });

  it('names itself', () => {
    expect(provider(new StubServer().withFlags({}, '"e1"')).metadata.name).toBe('FutureFlags');
  });

  it('carries the variant and reason through', async () => {
    const server = new StubServer().withFlags({ 'dark-mode': true }, '"e1"');
    const subject = provider(server);

    await subject.initialize();

    const details = await subject.resolveBooleanEvaluation('dark-mode', false, {});

    expect(details.value).toBe(true);
    expect(details.variant).toBe('on');
    expect(details.reason).toBe('STATIC');
    expect(details.errorCode).toBeUndefined();
  });

  it('reports a disabled flag as DISABLED rather than as an error', async () => {
    const server = new StubServer().withFlags({ 'dark-mode': false }, '"e1"');
    const subject = provider(server);

    await subject.initialize();

    const details = await subject.resolveBooleanEvaluation('dark-mode', true, {});

    expect(details.value).toBe(false);
    expect(details.reason).toBe('DISABLED');
    expect(details.errorCode).toBeUndefined();
  });

  it('reports a targeted flag that matched nothing as DEFAULT, not an error', async () => {
    // The reason mapping that matters most: nothing alerting on errorCode should see a deliberately
    // narrowed flag as an outage.
    const server = new StubServer().withRuleset(
      {
        environment: 'dev',
        flags: [{ key: 'new-checkout', isEnabled: true, targetedSegments: ['beta'] }],
        segments: [{ key: 'beta', included: ['user-17'], excluded: [], conditions: [] }],
      },
      '"e1"',
    );
    const subject = provider(server);

    await subject.initialize();

    const missed = await subject.resolveBooleanEvaluation('new-checkout', true, {
      targetingKey: 'user-99',
    });

    expect(missed.value).toBe(false);
    expect(missed.reason).toBe('DEFAULT');
    expect(missed.errorCode).toBeUndefined();

    const matched = await subject.resolveBooleanEvaluation('new-checkout', false, {
      targetingKey: 'user-17',
    });

    expect(matched.value).toBe(true);
    expect(matched.reason).toBe('TARGETING_MATCH');
  });

  it("returns the caller's own default for a flag it does not carry", async () => {
    const server = new StubServer().withFlags({ 'dark-mode': true }, '"e1"');
    const subject = provider(server);

    await subject.initialize();

    const details = await subject.resolveBooleanEvaluation('never-defined', true, {});

    expect(details.value).toBe(true);
    expect(details.errorCode).toBe('FLAG_NOT_FOUND');
  });

  it('says PROVIDER_NOT_READY rather than throwing when the server cannot be reached', async () => {
    // A flag service being down must not take down the application reading it.
    const server = new StubServer().unreachable();
    const subject = provider(server);

    await expect(subject.initialize()).resolves.toBeUndefined();

    const details = await subject.resolveBooleanEvaluation('dark-mode', true, {});

    expect(details.value).toBe(true);
    expect(details.errorCode).toBe('PROVIDER_NOT_READY');
  });

  it('answers a non-boolean evaluation with TYPE_MISMATCH and the default', async () => {
    const server = new StubServer().withFlags({ 'dark-mode': true }, '"e1"');
    const subject = provider(server);

    await subject.initialize();

    const details = await subject.resolveStringEvaluation('dark-mode', 'fallback', {});

    expect(details.value).toBe('fallback');
    expect(details.errorCode).toBe('TYPE_MISMATCH');
  });

  it('reports a misspelled key as missing rather than as a type problem', async () => {
    const server = new StubServer().withFlags({ 'dark-mode': true }, '"e1"');
    const subject = provider(server);

    await subject.initialize();

    const details = await subject.resolveStringEvaluation('never-defined', 'fallback', {});

    expect(details.errorCode).toBe('FLAG_NOT_FOUND');
  });

  it('sends the targeting key and drops what the platform cannot hold', async () => {
    const server = new StubServer().withRuleset(
      {
        environment: 'dev',
        flags: [{ key: 'f', isEnabled: true, targetedSegments: ['plan'] }],
        segments: [
          {
            key: 'plan',
            included: [],
            excluded: [],
            conditions: [{ attribute: 'plan', operator: 'equals', values: ['enterprise'] }],
          },
        ],
      },
      '"e1"',
    );
    const subject = provider(server);

    await subject.initialize();

    const details = await subject.resolveBooleanEvaluation('f', false, {
      targetingKey: 'user-17',
      plan: 'enterprise',
      // Neither of these can be an attribute here, and neither should stop the rest resolving.
      nested: { a: 1 },
      list: [1, 2],
    });

    expect(details.value).toBe(true);
    expect(details.reason).toBe('TARGETING_MATCH');
  });
});

describe('the browser-side OpenFeature provider', () => {
  function provider(server: StubServer) {
    return new FutureFlagsWebProvider({
      baseAddress: BASE,
      sdkKey: PUBLISHABLE_KEY,
      fetch: server.fetch,
    });
  }

  it('refuses a secret key at construction', () => {
    expect(() => new FutureFlagsWebProvider({ baseAddress: BASE, sdkKey: SECRET_KEY })).toThrow(
      /publishable/i,
    );
  });

  it('reads the OFREP route, not the deprecated one', async () => {
    // The reason this provider exists separately: POST /api/evaluation answers booleans with no
    // reasoning, and an OpenFeature client needs the reason.
    const server = new StubServer().withOfrepFlags([
      { key: 'dark-mode', value: true, variant: 'on', reason: 'STATIC' },
    ]);

    await provider(server).initialize({ targetingKey: 'user-17' });

    expect(server.requests[0]?.url).toContain('/ofrep/v1/evaluate/flags');
    expect(server.requests[0]?.method).toBe('POST');
  });

  it('flattens the context the way OFREP expects', async () => {
    const server = new StubServer().withOfrepFlags([]);

    await provider(server).initialize({ targetingKey: 'user-17', plan: 'enterprise' });

    // targetingKey alongside the custom fields, not nested under `attributes`.
    expect(JSON.parse(server.requests[0]?.body ?? '{}')).toEqual({
      context: { targetingKey: 'user-17', plan: 'enterprise' },
    });
  });

  it('carries the value, variant and reason through', async () => {
    const server = new StubServer().withOfrepFlags([
      { key: 'new-checkout', value: true, variant: 'on', reason: 'TARGETING_MATCH' },
    ]);
    const subject = provider(server);

    await subject.initialize({ targetingKey: 'user-17' });

    const details = subject.resolveBooleanEvaluation('new-checkout', false);

    expect(details.value).toBe(true);
    expect(details.variant).toBe('on');
    expect(details.reason).toBe('TARGETING_MATCH');
  });

  it("returns the caller's own default for a flag the environment does not carry", async () => {
    const server = new StubServer().withOfrepFlags([{ key: 'dark-mode', value: true }]);
    const subject = provider(server);

    await subject.initialize({});

    const details = subject.resolveBooleanEvaluation('never-defined', true);

    expect(details.value).toBe(true);
    expect(details.errorCode).toBe('FLAG_NOT_FOUND');
  });

  it('refetches when the subject changes and does not serve the old answers meanwhile', async () => {
    const server = new StubServer()
      .withOfrepFlags([{ key: 'f', value: true, variant: 'on', reason: 'TARGETING_MATCH' }])
      .withOfrepFlags([{ key: 'f', value: false, variant: 'off', reason: 'DEFAULT' }]);
    const subject = provider(server);

    await subject.initialize({ targetingKey: 'user-17' });
    expect(subject.resolveBooleanEvaluation('f', false).value).toBe(true);

    await subject.onContextChange({ targetingKey: 'user-17' }, { targetingKey: 'user-99' });

    // The old answers described somebody else.
    expect(subject.resolveBooleanEvaluation('f', true).value).toBe(false);
    expect(subject.resolveBooleanEvaluation('f', true).reason).toBe('DEFAULT');
    expect(server.callCount).toBe(2);
  });

  it('says PROVIDER_NOT_READY rather than throwing when the server cannot be reached', async () => {
    const server = new StubServer().unreachable();
    const subject = provider(server);

    await expect(subject.initialize({})).resolves.toBeUndefined();

    const details = subject.resolveBooleanEvaluation('dark-mode', true);

    expect(details.value).toBe(true);
    expect(details.errorCode).toBe('PROVIDER_NOT_READY');
  });

  it('reports a per-flag OFREP error rather than treating it as a value', async () => {
    const server = new StubServer().withOfrepFlags([
      { key: 'broken', errorCode: 'PARSE_ERROR' },
    ]);
    const subject = provider(server);

    await subject.initialize({});

    const details = subject.resolveBooleanEvaluation('broken', true);

    expect(details.value).toBe(true);
    expect(details.errorCode).toBe('PARSE_ERROR');
  });

  it('answers a non-boolean evaluation with TYPE_MISMATCH and the default', async () => {
    const server = new StubServer().withOfrepFlags([
      { key: 'dark-mode', value: true, variant: 'on', reason: 'STATIC' },
    ]);
    const subject = provider(server);

    await subject.initialize({});

    expect(subject.resolveStringEvaluation('dark-mode', 'fallback').errorCode).toBe('TYPE_MISMATCH');
    expect(subject.resolveStringEvaluation('dark-mode', 'fallback').value).toBe('fallback');
  });
});
