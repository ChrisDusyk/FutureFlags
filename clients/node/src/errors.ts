/**
 * A FutureFlags installation could not be read.
 *
 * This does not reach an `isEnabled` caller: reads fall back to the last good snapshot, or to the
 * default. It surfaces from `refresh()`, which is an explicit request and so reports what happened.
 *
 * Fields are declared and assigned rather than taken as constructor parameter properties, which
 * `erasableSyntaxOnly` rules out — nothing here may depend on TypeScript emitting code.
 */
export class FutureFlagsError extends Error {
  /** The HTTP status, when there was a response. Zero when the request never completed. */
  status: number;

  constructor(message: string, status = 0, options?: { cause?: unknown }) {
    super(message, options);
    this.name = 'FutureFlagsError';
    this.status = status;
  }
}

/**
 * A secret key in a browser. Thrown at construction rather than left to fail as a 401, because by
 * the time a request is made the mistake has already happened: the key is in the bundle.
 */
export class SecretKeyInBrowserError extends FutureFlagsError {
  constructor() {
    super(
      'FutureFlags: this is a secret SDK key (ffs_) and the client is running in a browser. ' +
        'Anything shipped to a browser can be read out of it, so this key should be treated as ' +
        'compromised — revoke it. Issue a publishable key (ffp_) in the console under ' +
        'Organization → Environments and use that here.',
    );
    this.name = 'SecretKeyInBrowserError';
  }
}
