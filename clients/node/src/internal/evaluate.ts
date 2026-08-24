import type { AttributeValue, NormalizedContext } from '../context.js';
import { EMPTY_CONTEXT, normalizeName } from '../context.js';

/**
 * The wire shapes of `GET /api/evaluation/ruleset`, and the engine that reads them.
 *
 * This is the one genuinely independent implementation of the platform's evaluation rule — the
 * server and the .NET client compile theirs from one shared C# source, which is not something
 * TypeScript can join. What holds this in step is `shared/evaluation/conformance/*.json`, run by
 * `test/conformance.test.ts` here and by both .NET suites there. A change to the rule that is not
 * also made here fails those.
 */
export interface RulesetCondition {
  readonly attribute: string;
  readonly operator: string;
  readonly values: readonly AttributeValue[];
}

export interface RulesetSegment {
  readonly key: string;
  readonly included: readonly string[];
  readonly excluded: readonly string[];
  readonly conditions: readonly RulesetCondition[];
}

export interface RulesetFlag {
  readonly key: string;
  readonly isEnabled: boolean;
  readonly targetedSegments: readonly string[];
}

export interface Ruleset {
  readonly environment: string;
  readonly flags: readonly RulesetFlag[];
  readonly segments: readonly RulesetSegment[];
}

/** Mirrors `ConditionOperatorNames` in the shared source. There is deliberately no regular
 * expression operator — see `shared/evaluation/README.md`. */
const OPERATORS = {
  equalTo: 'equals',
  oneOf: 'one-of',
  contains: 'contains',
  startsWith: 'starts-with',
  endsWith: 'ends-with',
  greaterThan: 'greater-than',
  greaterThanOrEqual: 'greater-than-or-equal',
  lessThan: 'less-than',
  lessThanOrEqual: 'less-than-or-equal',
} as const;

export function segmentsByKey(ruleset: Ruleset): ReadonlyMap<string, RulesetSegment> {
  const index = new Map<string, RulesetSegment>();

  for (const segment of ruleset.segments) {
    if (typeof segment?.key === 'string') {
      index.set(segment.key, segment);
    }
  }

  return index;
}

/**
 * Whether a context is in a segment.
 *
 * The order is the definition, not an optimisation. Exclusion is absolute, because the reason to
 * exclude somebody is usually that something is broken for them and an escape hatch anything can
 * overrule is not one. Inclusion then short-circuits the conditions, because naming a key is how
 * "one account I am debugging" is written and it should not also have to satisfy a rule meant for
 * everybody else.
 *
 * A segment with no included keys and no conditions matches **nobody**. The other reading — that a
 * definition with no restrictions restricts nothing — is defensible right up until a half-finished
 * segment is saved and turns a flag on for the world.
 */
export function matchesSegment(
  segment: RulesetSegment | undefined,
  context: NormalizedContext,
): boolean {
  if (!segment) {
    return false;
  }

  if (context.key !== null && segment.excluded.includes(context.key)) {
    return false;
  }

  if (context.key !== null && segment.included.includes(context.key)) {
    return true;
  }

  if (segment.conditions.length === 0) {
    return false;
  }

  return segment.conditions.every((condition) => satisfies(condition, context));
}

/**
 * Whether one condition holds. An absent attribute never satisfies anything: there is no default
 * value for a trait the application did not send, and inventing one would make a segment match
 * people nobody described.
 */
export function satisfies(condition: RulesetCondition, context: NormalizedContext): boolean {
  if (typeof condition?.attribute !== 'string' || typeof condition.operator !== 'string') {
    return false;
  }

  const actual = context.attributes.get(normalizeName(condition.attribute));

  if (actual === undefined) {
    return false;
  }

  switch (condition.operator) {
    // Strict equality, which is exactly the rule the platform wants: in JavaScript `'2' === 2` is
    // already false, so a type mismatch is a non-match without any special handling.
    case OPERATORS.equalTo:
    case OPERATORS.oneOf:
      return condition.values.some((candidate) => candidate === actual);

    case OPERATORS.contains:
      return text(condition, actual, (subject, candidate) => subject.includes(candidate));

    case OPERATORS.startsWith:
      return text(condition, actual, (subject, candidate) => subject.startsWith(candidate));

    case OPERATORS.endsWith:
      return text(condition, actual, (subject, candidate) => subject.endsWith(candidate));

    case OPERATORS.greaterThan:
      return numeric(condition, actual, (subject, candidate) => subject > candidate);

    case OPERATORS.greaterThanOrEqual:
      return numeric(condition, actual, (subject, candidate) => subject >= candidate);

    case OPERATORS.lessThan:
      return numeric(condition, actual, (subject, candidate) => subject < candidate);

    case OPERATORS.lessThanOrEqual:
      return numeric(condition, actual, (subject, candidate) => subject <= candidate);

    // An operator this build has never heard of. A client one release behind the console must not
    // throw over a segment it cannot evaluate, and must not guess either — so it does not match,
    // which is the same answer it would give if the condition simply failed.
    default:
      return false;
  }
}

/**
 * Whether a flag is on for one context. The whole rule, and there is not more to it in this
 * release: off beats everything; on with no targets is on for everyone, which is what a flag meant
 * before segments existed; on with targets needs a match.
 */
export function evaluateFlag(
  flag: RulesetFlag | undefined,
  segments: ReadonlyMap<string, RulesetSegment>,
  context: NormalizedContext,
): boolean {
  if (!flag || !flag.isEnabled) {
    return false;
  }

  if (flag.targetedSegments.length === 0) {
    return true;
  }

  return flag.targetedSegments.some((key) => {
    // A targeted segment this ruleset does not carry is a non-match rather than a failure. It
    // happens legitimately — a segment retired between the write that targeted it and this read —
    // and a flag that started throwing because somebody tidied up would be far worse than one that
    // quietly reaches nobody.
    const segment = segments.get(key);

    return segment !== undefined && matchesSegment(segment, context);
  });
}

/** Every flag in the ruleset, answered for one context. */
export function evaluateAll(
  ruleset: Ruleset | null | undefined,
  context: NormalizedContext = EMPTY_CONTEXT,
): Map<string, boolean> {
  const evaluated = new Map<string, boolean>();

  if (!ruleset) {
    return evaluated;
  }

  const segments = segmentsByKey(ruleset);

  for (const flag of ruleset.flags) {
    if (typeof flag?.key === 'string') {
      // Lowercased on the way in, matching how this client has always compared a flag key, so that
      // isEnabled('new-Checkout') keeps answering.
      evaluated.set(flag.key.toLowerCase(), evaluateFlag(flag, segments, context));
    }
  }

  return evaluated;
}

function text(
  condition: RulesetCondition,
  actual: AttributeValue,
  predicate: (subject: string, candidate: string) => boolean,
): boolean {
  // A string operator over a non-string attribute is a non-match, never a rendering. The point of
  // typing these values is that `accountAge contains '4'` is not a question the platform answers.
  if (typeof actual !== 'string') {
    return false;
  }

  return condition.values.some(
    (candidate) => typeof candidate === 'string' && predicate(actual, candidate),
  );
}

function numeric(
  condition: RulesetCondition,
  actual: AttributeValue,
  predicate: (subject: number, candidate: number) => boolean,
): boolean {
  if (typeof actual !== 'number') {
    return false;
  }

  return condition.values.some(
    (candidate) => typeof candidate === 'number' && predicate(actual, candidate),
  );
}
