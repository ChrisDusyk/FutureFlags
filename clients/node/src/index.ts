export type { FutureFlagsCacheStore } from './cache.js';
export { createFutureFlagsClient, type FutureFlagsClient } from './client.js';
export type { AttributeValue, FlagContext } from './context.js';
export { FutureFlagsError, SecretKeyInBrowserError } from './errors.js';
export type { FutureFlagsOptions } from './options.js';

// What `client.resolve()` answers with. Types and two constants only — nothing here reaches for an
// OpenFeature SDK, so the root import stays dependency-free and a consumer who only wants
// `isEnabled` pays for neither peer dependency.
export {
  asBoolean,
  VARIANT_OFF,
  VARIANT_ON,
  type EvaluationErrorCode,
  type EvaluationReason,
  type FlagResolution,
  type FlagValue,
  type FlagValueType,
} from './resolution.js';
