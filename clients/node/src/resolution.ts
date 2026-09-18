/**
 * OpenFeature's resolution vocabulary, mirrored from `shared/evaluation/dotnet`.
 *
 * The server and the .NET client compile their copy from one shared C# source; this is the
 * independent TypeScript implementation of the same thing, held in step by
 * `shared/evaluation/conformance/flags.json`. A reason or error code added on one side and not the
 * other fails those vectors.
 */

/** Which of the four things a flag's value is. */
export type FlagValueType = 'boolean' | 'string' | 'number' | 'object';

/** A flag's value. Only `boolean` is authored by this build; the rest exist so the wire shape
 * never has to change again. */
export type FlagValue = boolean | string | number | JsonObject;

/** A JSON object or array, as an object-typed flag would carry. */
export type JsonObject = { readonly [key: string]: unknown } | readonly unknown[];

/**
 * Why an evaluation answered what it did.
 *
 * A plain string union with a `(string & {})` escape, not a closed enum: the OpenFeature
 * specification lets a provider return a reason of its own, and a client that cannot represent one
 * cannot pass it through.
 */
export type EvaluationReason =
  | 'STATIC'
  | 'DEFAULT'
  | 'TARGETING_MATCH'
  | 'SPLIT'
  | 'CACHED'
  | 'DISABLED'
  | 'UNKNOWN'
  | 'STALE'
  | 'ERROR'
  | (string & {});

/** Why an evaluation failed. Populated only alongside a reason of `ERROR`. */
export type EvaluationErrorCode =
  | 'PROVIDER_NOT_READY'
  | 'FLAG_NOT_FOUND'
  | 'PARSE_ERROR'
  | 'TYPE_MISMATCH'
  | 'TARGETING_KEY_MISSING'
  | 'INVALID_CONTEXT'
  | 'PROVIDER_FATAL'
  | 'GENERAL'
  | (string & {});

/** The variant names a boolean flag carries. Every flag in this build has exactly these two. */
export const VARIANT_ON = 'on';
/** @see VARIANT_ON */
export const VARIANT_OFF = 'off';

/**
 * One flag's answer for one context, with the reasoning attached.
 *
 * This is OpenFeature's `resolution details` structure and the field names are its names, so a
 * provider can hand them straight across rather than inventing a reason from a bare boolean.
 */
export interface FlagResolution {
  readonly value: FlagValue;
  readonly variant: string | null;
  readonly reason: EvaluationReason;
  readonly errorCode: EvaluationErrorCode | null;
  readonly errorMessage: string | null;
  readonly flagMetadata: Readonly<Record<string, string | number | boolean>>;
}

/** Shared empty metadata. The specification requires an empty record rather than an absent one, so
 * every consumer can read the field without a guard. */
export const NO_FLAG_METADATA: Readonly<Record<string, string | number | boolean>> =
  Object.freeze({});

/**
 * The boolean a resolution carries, or `defaultValue` when it carries something else or failed.
 * The bridge every boolean-typed caller crosses, and why a type mismatch never throws.
 */
export function asBoolean(resolution: FlagResolution, defaultValue = false): boolean {
  return resolution.errorCode === null && typeof resolution.value === 'boolean'
    ? resolution.value
    : defaultValue;
}
