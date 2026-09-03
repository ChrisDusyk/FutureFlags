import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

import { normalizeContext, type FlagContext } from '../src/context.js';
import {
  evaluateAll,
  matchesSegment,
  resolveAll,
  resolveFlag,
  segmentsByKey,
  type Ruleset,
  type RulesetSegment,
} from '../src/internal/evaluate.js';
import { asBoolean, type FlagValue } from '../src/resolution.js';

/**
 * The shared conformance vectors.
 *
 * This suite is the reason those files are JSON rather than a C# theory. The server and the .NET
 * client compile one evaluator from one shared source, so running the vectors there really checks
 * that three compilations of the same code agree. This is the only place they check a genuinely
 * separate implementation, which is what makes it the one that can catch the rule drifting.
 */
interface SegmentCase {
  name: string;
  segment: RulesetSegment;
  context: FlagContext;
  matches: boolean;
}

interface ResolutionVector {
  value: FlagValue;
  variant: string | null;
  reason: string;
  /** Absent in every normal case, and asserted as absent — see the file's own expectedNote. */
  errorCode?: string;
}

interface FlagCase {
  name: string;
  ruleset: Ruleset;
  context: FlagContext;
  expected: Record<string, ResolutionVector>;
  /** Keys the ruleset deliberately does not carry. */
  missing?: string[];
}

function load<TCase>(fileName: string): TCase[] {
  const path = new URL(`../../../shared/evaluation/conformance/${fileName}`, import.meta.url);
  const parsed = JSON.parse(readFileSync(path, 'utf8')) as { cases: TCase[] };

  // An empty file would make every case below vacuously pass, which is the one failure this suite
  // cannot afford.
  expect(parsed.cases.length).toBeGreaterThan(0);

  return parsed.cases;
}

describe('segment conformance', () => {
  const cases = load<SegmentCase>('segments.json');

  it.each(cases.map((vector) => [vector.name, vector] as const))('%s', (_name, vector) => {
    expect(matchesSegment(vector.segment, normalizeContext(vector.context))).toBe(vector.matches);
  });
});

describe('flag conformance', () => {
  const cases = load<FlagCase>('flags.json');

  it.each(cases.map((vector) => [vector.name, vector] as const))('%s', (_name, vector) => {
    const context = normalizeContext(vector.context);
    const resolved = resolveAll(vector.ruleset, context);

    // Compared both ways round: an engine answering for a flag the vector never mentioned would
    // otherwise pass, and that is exactly the drift this file exists to catch.
    expect([...resolved.keys()].sort()).toEqual(Object.keys(vector.expected).sort());

    for (const [key, expected] of Object.entries(vector.expected)) {
      const actual = resolved.get(key);

      expect(actual, `No answer for '${key}'.`).toBeDefined();
      expect(actual!.value).toEqual(expected.value);
      expect(actual!.variant).toBe(expected.variant);
      expect(actual!.reason).toBe(expected.reason);

      // Asserted even where the vector omits it, which is the whole reason for version 2: a normal
      // resolution must carry no error code, and a reason-only assertion would let a regression set
      // one alongside DEFAULT without anything going red.
      expect(actual!.errorCode).toBe(expected.errorCode ?? null);
    }

    // evaluateAll is now a reading of resolveAll, so the boolean surface every released version of
    // this client depends on has to keep agreeing with it.
    const evaluated = evaluateAll(vector.ruleset, context);

    for (const [key, resolution] of resolved) {
      expect(evaluated.get(key)).toBe(asBoolean(resolution));
    }

    for (const key of vector.missing ?? []) {
      // The vector has to be telling the truth about the key being absent, or the assertions below
      // would pass against a flag that simply resolved to something else.
      const flag = vector.ruleset.flags.find(
        (candidate) => candidate.key.toLowerCase() === key.toLowerCase(),
      );

      expect(flag).toBeUndefined();

      const resolution = resolveFlag(flag, segmentsByKey(vector.ruleset), context);

      expect(resolution.reason).toBe('ERROR');
      expect(resolution.errorCode).toBe('FLAG_NOT_FOUND');
      expect(resolution.variant).toBeNull();

      // Still false to a boolean caller, which is what this client has always answered for a key it
      // does not carry.
      expect(asBoolean(resolution)).toBe(false);
    }
  });
});
