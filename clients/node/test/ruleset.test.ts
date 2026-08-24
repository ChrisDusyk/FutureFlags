import { describe, expect, it } from 'vitest';

import { isRuleset } from '../src/internal/ruleset.js';

/**
 * `isRuleset` is the network boundary: a payload that is almost right — the correct top-level
 * shape but a wrong element type somewhere inside it — has to fail here rather than pass through
 * and evaluate as an ordinary "not targeted" further down. Every case below is something that
 * would otherwise pass a shallow `Array.isArray` check and only reveal itself as a silent
 * non-match at evaluation time.
 */
describe('isRuleset', () => {
  const valid = {
    environment: 'dev',
    flags: [{ key: 'new-checkout', isEnabled: true, targetedSegments: ['beta-testers'] }],
    segments: [
      {
        key: 'beta-testers',
        included: ['user-17'],
        excluded: ['user-99'],
        conditions: [{ attribute: 'plan', operator: 'equals', values: ['pro', 47, true] }],
      },
    ],
  };

  it('accepts a well-formed ruleset', () => {
    expect(isRuleset(valid)).toBe(true);
  });

  it('accepts an empty ruleset', () => {
    expect(isRuleset({ environment: 'dev', flags: [], segments: [] })).toBe(true);
  });

  it('rejects a non-object', () => {
    expect(isRuleset(null)).toBe(false);
    expect(isRuleset('dev')).toBe(false);
    expect(isRuleset(42)).toBe(false);
  });

  it('rejects a targetedSegments entry that is not a string', () => {
    const malformed = {
      ...valid,
      flags: [{ key: 'new-checkout', isEnabled: true, targetedSegments: [42] }],
    };

    expect(isRuleset(malformed)).toBe(false);
  });

  it('rejects an included-key entry that is not a string', () => {
    const malformed = {
      ...valid,
      segments: [{ ...valid.segments[0], included: [{ not: 'a string' }] }],
    };

    expect(isRuleset(malformed)).toBe(false);
  });

  it('rejects an excluded-key entry that is not a string', () => {
    const malformed = {
      ...valid,
      segments: [{ ...valid.segments[0], excluded: [null] }],
    };

    expect(isRuleset(malformed)).toBe(false);
  });

  it('rejects a condition whose attribute is not a string', () => {
    const malformed = {
      ...valid,
      segments: [
        {
          ...valid.segments[0],
          conditions: [{ attribute: 7, operator: 'equals', values: ['pro'] }],
        },
      ],
    };

    expect(isRuleset(malformed)).toBe(false);
  });

  it('rejects a condition value that is not a string, number, or boolean', () => {
    const malformed = {
      ...valid,
      segments: [
        {
          ...valid.segments[0],
          conditions: [{ attribute: 'plan', operator: 'equals', values: [{ nested: true }] }],
        },
      ],
    };

    expect(isRuleset(malformed)).toBe(false);
  });

  it('rejects a flag missing isEnabled', () => {
    const malformed = { ...valid, flags: [{ key: 'new-checkout', targetedSegments: [] }] };

    expect(isRuleset(malformed)).toBe(false);
  });

  it('rejects flags that is not an array', () => {
    expect(isRuleset({ ...valid, flags: 'nope' })).toBe(false);
  });
});
