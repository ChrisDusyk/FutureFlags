import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

import { normalizeContext, type FlagContext } from '../src/context.js';
import {
  evaluateAll,
  matchesSegment,
  type Ruleset,
  type RulesetSegment,
} from '../src/internal/evaluate.js';

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

interface FlagCase {
  name: string;
  ruleset: Ruleset;
  context: FlagContext;
  expected: Record<string, boolean>;
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
    const evaluated = evaluateAll(vector.ruleset, normalizeContext(vector.context));

    // Compared both ways round: an engine answering for a flag the vector never mentioned would
    // otherwise pass, and that is exactly the drift this file exists to catch.
    expect(Object.fromEntries(evaluated)).toEqual(vector.expected);
  });
});
