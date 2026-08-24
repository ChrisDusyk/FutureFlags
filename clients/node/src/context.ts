/**
 * A trait describing whoever a flag is being read for.
 *
 * Exactly the three types JSON has, because that is what the platform stores and compares. There is
 * no coercion between them anywhere in the system: a segment written against the number `30` never
 * matches the string `'30'`.
 */
export type AttributeValue = string | number | boolean;

/**
 * Who a flag is being read for.
 *
 * Both members are optional, and an empty context is a real thing to send rather than a mistake —
 * it is what an application that has not identified anybody has to say, and it matches no segment.
 */
export interface FlagContext {
  /** What your application calls this subject: a user id, an account id, a device. */
  key?: string;
  /** Whatever you know about them that a segment might be written against. */
  attributes?: Record<string, AttributeValue>;
}

/**
 * A context with its attribute names folded to lowercase and anything unusable dropped, which is
 * the only form the evaluator ever sees.
 *
 * The folding matches what the server does to a segment's attribute names, so a segment written
 * against `plan` matches an application that sends `Plan`. Values and the key are left exactly as
 * given and compared exactly: case-insensitive comparison across JavaScript and .NET means picking
 * a culture, and they do not agree on every alphabet.
 */
export interface NormalizedContext {
  readonly key: string | null;
  readonly attributes: ReadonlyMap<string, AttributeValue>;
}

export const EMPTY_CONTEXT: NormalizedContext = {
  key: null,
  attributes: new Map(),
};

export function normalizeContext(context: FlagContext | null | undefined): NormalizedContext {
  if (!context) {
    return EMPTY_CONTEXT;
  }

  const attributes = new Map<string, AttributeValue>();

  for (const [name, value] of Object.entries(context.attributes ?? {})) {
    // Anything else — null, an array, an object, a Date — is dropped rather than stringified. A
    // silently coerced attribute is an attribute that matches something nobody wrote a rule for.
    if (typeof value === 'string' || typeof value === 'boolean') {
      attributes.set(normalizeName(name), value);
    } else if (typeof value === 'number' && Number.isFinite(value)) {
      // NaN and the infinities have no JSON representation, so they could never have reached the
      // server intact; dropping them here means the local and remote paths agree about that.
      attributes.set(normalizeName(name), value);
    }
  }

  return {
    key: typeof context.key === 'string' && context.key.length > 0 ? context.key : null,
    attributes,
  };
}

export function normalizeName(name: string): string {
  return name.trim().toLowerCase();
}

/**
 * A context laid over another. Everything in `context` wins, and anything only `defaults` carries
 * is kept — which is what lets an application set the traits that never change once, at
 * construction, and still describe a user per call.
 */
export function withDefaults(
  context: NormalizedContext,
  defaults: NormalizedContext | null,
): NormalizedContext {
  if (!defaults || (defaults.key === null && defaults.attributes.size === 0)) {
    return context;
  }

  const attributes = new Map(defaults.attributes);

  for (const [name, value] of context.attributes) {
    attributes.set(name, value);
  }

  return { key: context.key ?? defaults.key, attributes };
}

/**
 * A stable string identifying this context, used to key a cached answer.
 *
 * Every part is length-prefixed and every value carries its type, so no two different contexts can
 * produce the same fingerprint — `{ab: 'c'}` and `{a: 'bc'}` would otherwise collide, and a client
 * would serve one person's answers to another.
 */
export function fingerprintContext(context: NormalizedContext): string {
  const parts: string[] = [prefixed(context.key ?? '')];

  for (const name of [...context.attributes.keys()].sort()) {
    const value = context.attributes.get(name)!;

    parts.push(prefixed(name), prefixed(typeTag(value) + String(value)));
  }

  return parts.join('');
}

function typeTag(value: AttributeValue): string {
  if (typeof value === 'string') {
    return 's:';
  }

  return typeof value === 'number' ? 'n:' : 'b:';
}

function prefixed(part: string): string {
  return `${part.length}:${part}`;
}
