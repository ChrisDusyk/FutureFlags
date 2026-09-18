import type {
  EvaluationContext,
  JsonValue,
  Provider,
  ResolutionDetails,
} from '@openfeature/web-sdk';

import { evaluateBulk, type OfrepFlag, type OfrepSnapshot } from '../internal/ofrep.js';
import { deadline } from '../internal/runtime.js';
import { PUBLISHABLE_KEY_PREFIX, resolveOptions, type FutureFlagsOptions } from '../options.js';
import { NO_FLAG_METADATA, type FlagResolution } from '../resolution.js';
import {
  ERROR_CODES,
  toBooleanResolution,
  toFlagContext,
  toUnsupportedResolution,
  type OpenFeatureContext,
} from './shared.js';

/**
 * A FutureFlags-backed provider for `@openfeature/web-sdk`.
 *
 * The web SDK's model is static context: one subject at a time, evaluated up front, with resolution
 * a synchronous lookup afterwards. That is exactly what a publishable key buys — the context goes
 * up, values come back, and segment definitions never reach the bundle.
 *
 * ```ts
 * import { OpenFeature } from '@openfeature/web-sdk';
 * import { FutureFlagsWebProvider } from '@futureflags/client/openfeature/web';
 *
 * await OpenFeature.setContext({ targetingKey: user.id, plan: user.plan });
 * await OpenFeature.setProviderAndWait(
 *   new FutureFlagsWebProvider({
 *     baseAddress: 'https://flags.example.com',
 *     sdkKey: 'ffp_prod_...',
 *   }),
 * );
 * ```
 *
 * It reads the OFREP route rather than the deprecated `POST /api/evaluation`, which is what lets it
 * report a real reason and variant rather than a bare boolean.
 */
export class FutureFlagsWebProvider implements Provider {
  readonly metadata = { name: 'FutureFlags' } as const;

  readonly runsOn = 'client' as const;

  private readonly options: ReturnType<typeof resolveOptions>;

  private readonly lifetime = new AbortController();

  private snapshot: OfrepSnapshot | null = null;

  /**
   * @param options The same options `createFutureFlagsClient` takes. The SDK key must be a
   *   publishable (`ffp_`) one: a secret key must never reach a browser, and the server refuses one
   *   that arrives with an `Origin` header anyway.
   */
  constructor(options: FutureFlagsOptions) {
    if (!options.sdkKey?.startsWith(PUBLISHABLE_KEY_PREFIX)) {
      throw new TypeError(
        `FutureFlags: the web provider needs a publishable ('${PUBLISHABLE_KEY_PREFIX}') SDK key. ` +
          'A secret key shipped to a browser is published whatever it is used for; use ' +
          'FutureFlagsServerProvider with it instead.',
      );
    }

    this.options = resolveOptions(options);
  }

  /** Evaluates everything for the context the SDK was given, before any resolution is asked for. */
  async initialize(context?: EvaluationContext): Promise<void> {
    await this.load(context);
  }

  /**
   * Re-evaluates when the subject changes, which is the whole shape of a static-context SDK.
   *
   * The snapshot is dropped first rather than replaced on success: after `setContext` the old
   * answers describe somebody else, and serving them until the new ones arrive would be worse than
   * serving defaults for a moment.
   */
  async onContextChange(_previous: EvaluationContext, next: EvaluationContext): Promise<void> {
    this.snapshot = null;

    await this.load(next);
  }

  async onClose(): Promise<void> {
    this.lifetime.abort();
  }

  resolveBooleanEvaluation(flagKey: string, defaultValue: boolean): ResolutionDetails<boolean> {
    return toBooleanResolution(this.lookup(flagKey), defaultValue) as ResolutionDetails<boolean>;
  }

  resolveStringEvaluation(flagKey: string, defaultValue: string): ResolutionDetails<string> {
    return toUnsupportedResolution(
      this.lookup(flagKey),
      defaultValue,
      'string',
    ) as ResolutionDetails<string>;
  }

  resolveNumberEvaluation(flagKey: string, defaultValue: number): ResolutionDetails<number> {
    return toUnsupportedResolution(
      this.lookup(flagKey),
      defaultValue,
      'number',
    ) as ResolutionDetails<number>;
  }

  resolveObjectEvaluation<T extends JsonValue>(flagKey: string, defaultValue: T): ResolutionDetails<T> {
    return toUnsupportedResolution(
      this.lookup(flagKey),
      defaultValue,
      'object',
    ) as ResolutionDetails<T>;
  }

  /**
   * Fetches, absorbing failure.
   *
   * The specification allows an initialize failure to terminate abnormally; this provider follows
   * the rest of the package instead, where a flag service being unreachable must not take down the
   * application reading it. Every resolution then says `PROVIDER_NOT_READY` until a later context
   * change succeeds.
   */
  private async load(context: EvaluationContext | undefined): Promise<void> {
    const attempt = deadline(this.lifetime.signal, this.options.timeout);

    try {
      const fetched = await evaluateBulk(
        this.options,
        toFlagContext(context as OpenFeatureContext | undefined),
        this.snapshot,
        attempt,
      );

      // Null is a 304 — what is held is still current for this context.
      this.snapshot = fetched ?? this.snapshot;
    } catch {
      // Deliberately absorbed — see above.
    } finally {
      attempt.settle();
    }
  }

  private lookup(flagKey: string): FlagResolution {
    if (this.snapshot === null) {
      return {
        value: false,
        variant: null,
        reason: 'ERROR',
        errorCode: ERROR_CODES.providerNotReady,
        errorMessage: 'No flags have been loaded yet.',
        flagMetadata: NO_FLAG_METADATA,
      };
    }

    const flag = this.snapshot.flags.get(flagKey.toLowerCase());

    if (flag === undefined) {
      return {
        value: false,
        variant: null,
        reason: 'ERROR',
        errorCode: ERROR_CODES.flagNotFound,
        errorMessage: 'No flag by that key exists in this environment.',
        flagMetadata: NO_FLAG_METADATA,
      };
    }

    return toResolution(flag);
  }
}

/** One OFREP flag entry as this package's resolution. */
function toResolution(flag: OfrepFlag): FlagResolution {
  if (flag.errorCode !== undefined || flag.value === undefined) {
    return {
      value: false,
      variant: null,
      reason: 'ERROR',
      errorCode: flag.errorCode ?? ERROR_CODES.general,
      errorMessage: flag.errorDetails ?? null,
      flagMetadata: NO_FLAG_METADATA,
    };
  }

  return {
    value: flag.value,
    variant: flag.variant ?? null,
    reason: flag.reason ?? 'UNKNOWN',
    errorCode: null,
    errorMessage: null,
    flagMetadata: NO_FLAG_METADATA,
  };
}
