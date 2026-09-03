import type { FlagContext } from '../context.js';
import type { EvaluationErrorCode, FlagResolution, FlagValue } from '../resolution.js';

/**
 * What the two providers share: turning an OpenFeature evaluation context into this platform's, and
 * turning a resolution into the shape both SDKs expect.
 *
 * Not exported from the package. Two copies of this would be two chances for the server-side and
 * browser-side providers to answer the same question differently, which is the failure the shared
 * evaluation source exists to prevent one layer down.
 */

/** OpenFeature's context: a targeting key plus arbitrary fields. */
export interface OpenFeatureContext {
  readonly targetingKey?: string;
  readonly [field: string]: unknown;
}

/**
 * OpenFeature's context, as this platform's.
 *
 * Values this platform cannot hold — objects, arrays, null — are dropped rather than rejected,
 * matching what the server's own OFREP routes do with the same context: absent, and absent never
 * matches. Failing instead would mean one unrelated field stops every flag resolving. A `Date`
 * becomes ISO-8601 text, which is the only form three runtimes compare the same way.
 */
export function toFlagContext(context: OpenFeatureContext | undefined): FlagContext {
  if (!context) {
    return {};
  }

  const attributes: Record<string, string | number | boolean> = {};

  for (const [field, value] of Object.entries(context)) {
    if (field === 'targetingKey') {
      continue;
    }

    if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
      attributes[field] = value;
      continue;
    }

    if (value instanceof Date) {
      attributes[field] = value.toISOString();
    }
  }

  return { key: context.targetingKey, attributes };
}

/** The error codes both OpenFeature SDKs name, as this package spells them. */
export const ERROR_CODES = {
  flagNotFound: 'FLAG_NOT_FOUND',
  parseError: 'PARSE_ERROR',
  typeMismatch: 'TYPE_MISMATCH',
  targetingKeyMissing: 'TARGETING_KEY_MISSING',
  invalidContext: 'INVALID_CONTEXT',
  providerNotReady: 'PROVIDER_NOT_READY',
  providerFatal: 'PROVIDER_FATAL',
  general: 'GENERAL',
} as const;

/** What an OpenFeature SDK's `ResolutionDetails` looks like, minus the SDK's own imports. */
export interface ProviderResolution<T> {
  value: T;
  variant?: string;
  reason?: string;
  errorCode?: EvaluationErrorCode;
  errorMessage?: string;
}

/**
 * A boolean resolution, or the caller's default with the reason it could not be served.
 *
 * A flag whose value is not boolean is a type mismatch rather than a coerced answer. There are none
 * today — every flag this platform can author is boolean — but the ruleset already carries a value
 * type, so this is the honest reading rather than an unreachable branch pretending to be one.
 */
export function toBooleanResolution(
  resolution: FlagResolution,
  defaultValue: boolean,
): ProviderResolution<boolean> {
  if (resolution.errorCode !== null) {
    return {
      value: defaultValue,
      reason: 'ERROR',
      errorCode: resolution.errorCode,
      errorMessage: resolution.errorMessage ?? undefined,
    };
  }

  if (typeof resolution.value !== 'boolean') {
    return typeMismatch(defaultValue, resolution.value, 'boolean');
  }

  return {
    value: resolution.value,
    variant: resolution.variant ?? undefined,
    reason: resolution.reason,
  };
}

/**
 * Every non-boolean resolution.
 *
 * A caller asking for a string is asking for something no flag here can be, so it gets
 * `TYPE_MISMATCH` and its own default. Inventing a value from a boolean would be worse than the
 * honest refusal. A key the ruleset does not carry is still reported missing rather than
 * mismatched — different mistakes, and a caller can only fix the one they are told about.
 */
export function toUnsupportedResolution<T>(
  resolution: FlagResolution,
  defaultValue: T,
  requested: string,
): ProviderResolution<T> {
  if (resolution.errorCode !== null) {
    return {
      value: defaultValue,
      reason: 'ERROR',
      errorCode: resolution.errorCode,
      errorMessage: resolution.errorMessage ?? undefined,
    };
  }

  return typeMismatch(defaultValue, resolution.value, requested);
}

function typeMismatch<T>(
  defaultValue: T,
  actual: FlagValue,
  requested: string,
): ProviderResolution<T> {
  return {
    value: defaultValue,
    reason: 'ERROR',
    errorCode: ERROR_CODES.typeMismatch,
    errorMessage: `The flag holds a ${typeof actual} value and was asked for a ${requested} one.`,
  };
}
