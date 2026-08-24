/**
 * Stands in for a FeatureFlags installation. Records what it was asked, because the request is half
 * of what this package does — the bearer token and the `If-None-Match` are the whole of the
 * conditional-fetch contract.
 */
export interface StubRequest {
  url: string;
  method: string;
  /** The serialized body, for the POST path — that is where the context travels. */
  body: string | null;
  headers: Headers;
}

export class StubServer {
  readonly requests: StubRequest[] = [];

  private readonly answers: Array<() => Response | Promise<Response>> = [];
  private last: (() => Response | Promise<Response>) | null = null;

  /** How long the stub takes to answer. Stands in for a server that accepts and then goes quiet. */
  delay = 0;

  get callCount(): number {
    return this.requests.length;
  }

  answers_(answer: () => Response | Promise<Response>): this {
    this.answers.push(answer);

    return this;
  }

  /**
   * Answers with a ruleset in which every named flag is on or off and reaches everyone. Most tests
   * here are about the request, the snapshot, or the cache tier rather than about targeting, and
   * spelling out a whole ruleset for each would bury what they actually check — `withRuleset` is
   * for the ones that are about targeting.
   */
  withFlags(flags: Record<string, boolean>, etag: string, environment = 'dev'): this {
    return this.withRuleset(
      {
        environment,
        flags: Object.entries(flags).map(([key, isEnabled]) => ({
          key,
          isEnabled,
          targetedSegments: [],
        })),
        segments: [],
      },
      etag,
    );
  }

  withRuleset(ruleset: unknown, etag: string): this {
    return this.answers_(
      () =>
        new Response(JSON.stringify(ruleset), {
          status: 200,
          headers: { 'content-type': 'application/json', etag },
        }),
    );
  }

  /** Answers the way `POST /api/evaluation` does: booleans for one context. */
  withAnswers(flags: Record<string, boolean>, rulesetVersion = '"r1"', environment = 'dev'): this {
    return this.answers_(
      () =>
        new Response(JSON.stringify({ environment, rulesetVersion, flags }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
    );
  }

  notModified(): this {
    return this.answers_(() => new Response(null, { status: 304 }));
  }

  withStatus(status: number, body?: unknown): this {
    return this.answers_(
      () =>
        new Response(body === undefined ? null : JSON.stringify(body), {
          status,
          headers: body === undefined ? undefined : { 'content-type': 'application/json' },
        }),
    );
  }

  unreachable(): this {
    return this.answers_(() => {
      throw new TypeError('fetch failed');
    });
  }

  /** The fetch to hand the client. The last answer queued repeats once the queue runs dry. */
  get fetch(): typeof globalThis.fetch {
    return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      this.requests.push({
        url: String(input),
        method: init?.method ?? 'GET',
        body: typeof init?.body === 'string' ? init.body : null,
        headers: new Headers(init?.headers),
      });

      if (this.delay > 0) {
        await new Promise((resolve, reject) => {
          const timer = setTimeout(resolve, this.delay);

          init?.signal?.addEventListener('abort', () => {
            clearTimeout(timer);
            reject(init.signal?.reason ?? new Error('aborted'));
          });
        });
      }

      const answer = this.answers.shift() ?? this.last;

      if (!answer) {
        throw new Error('The stub was asked before it was told what to say.');
      }

      this.last = answer;

      return answer();
    };
  }
}
