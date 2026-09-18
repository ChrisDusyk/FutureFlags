import type {
  EvaluationContext,
  JsonValue,
  Provider,
  ResolutionDetails,
} from '@openfeature/server-sdk';

import { createFutureFlagsClient, type FutureFlagsClient } from '../client.js';
import type { FutureFlagsOptions } from '../options.js';
import { SECRET_KEY_PREFIX } from '../options.js';
import {
  toBooleanResolution,
  toFlagContext,
  toUnsupportedResolution,
  type OpenFeatureContext,
} from './shared.js';

/**
 * A FutureFlags-backed provider for `@openfeature/server-sdk`.
 *
 * The server SDK's model is dynamic context: every evaluation carries its own, because a server
 * answers for a different person on every request. That is exactly what a secret key buys — the
 * ruleset is held in this process and evaluated locally, so a thousand users cost a thousand
 * lookups rather than a thousand requests.
 *
 * ```ts
 * import { OpenFeature } from '@openfeature/server-sdk';
 * import { FutureFlagsServerProvider } from '@futureflags/client/openfeature/server';
 *
 * await OpenFeature.setProviderAndWait(
 *   new FutureFlagsServerProvider({
 *     baseAddress: 'https://flags.example.com',
 *     sdkKey: process.env.FUTUREFLAGS_SDK_KEY!,
 *   }),
 * );
 * ```
 *
 * A thin wrapper over the client's own `resolve`, not a second evaluator: the reasons and variants
 * it reports come from the shared rule the server and the .NET client answer from.
 */
export class FutureFlagsServerProvider implements Provider {
  readonly metadata = { name: 'FutureFlags' } as const;

  readonly runsOn = 'server' as const;

  private readonly client: FutureFlagsClient;

  /**
   * @param options The same options `createFutureFlagsClient` takes. The SDK key must be a secret
   *   (`ffs_`) one — this provider evaluates in-process, which is what a publishable key is
   *   specifically not allowed to do. Use `FutureFlagsWebProvider` for those.
   */
  constructor(options: FutureFlagsOptions) {
    // Refused here rather than at the first evaluation. The server would answer 403 to the ruleset
    // request and the provider would come up looking healthy while resolving nothing — the useful
    // moment to say so is the line that configured it.
    if (!options.sdkKey?.startsWith(SECRET_KEY_PREFIX)) {
      throw new TypeError(
        `FutureFlags: the server provider needs a secret ('${SECRET_KEY_PREFIX}') SDK key, ` +
          'because it evaluates flags in this process. Use FutureFlagsWebProvider with a ' +
          "publishable ('ffp_') key instead.",
      );
    }

    this.client = createFutureFlagsClient(options);
  }

  /**
   * Loads the first ruleset, so evaluations made after `setProviderAndWait` returns are answered
   * from real flags rather than from defaults.
   *
   * A failure is absorbed rather than thrown. The specification allows an initialize failure to
   * terminate abnormally, but this client's whole posture is that an unreachable flag service must
   * not take down the application reading it — so the provider comes up and every resolution says
   * `PROVIDER_NOT_READY` until a background refresh succeeds.
   */
  async initialize(): Promise<void> {
    await this.client.refresh().catch(() => {});
  }

  async onClose(): Promise<void> {
    this.client.close();
  }

  async resolveBooleanEvaluation(
    flagKey: string,
    defaultValue: boolean,
    context: EvaluationContext,
  ): Promise<ResolutionDetails<boolean>> {
    const resolution = await this.client.resolve(
      flagKey,
      toFlagContext(context as OpenFeatureContext),
    );

    return toBooleanResolution(resolution, defaultValue) as ResolutionDetails<boolean>;
  }

  async resolveStringEvaluation(
    flagKey: string,
    defaultValue: string,
    context: EvaluationContext,
  ): Promise<ResolutionDetails<string>> {
    return this.unsupported(flagKey, defaultValue, 'string', context);
  }

  async resolveNumberEvaluation(
    flagKey: string,
    defaultValue: number,
    context: EvaluationContext,
  ): Promise<ResolutionDetails<number>> {
    return this.unsupported(flagKey, defaultValue, 'number', context);
  }

  async resolveObjectEvaluation<T extends JsonValue>(
    flagKey: string,
    defaultValue: T,
    context: EvaluationContext,
  ): Promise<ResolutionDetails<T>> {
    return this.unsupported(flagKey, defaultValue, 'object', context);
  }

  private async unsupported<T>(
    flagKey: string,
    defaultValue: T,
    requested: string,
    context: EvaluationContext,
  ): Promise<ResolutionDetails<T>> {
    const resolution = await this.client.resolve(
      flagKey,
      toFlagContext(context as OpenFeatureContext),
    );

    return toUnsupportedResolution(resolution, defaultValue, requested) as ResolutionDetails<T>;
  }
}
