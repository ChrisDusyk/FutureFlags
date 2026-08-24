# @featureflags/client

Reads feature flags from a self-hosted [FeatureFlags](https://github.com/ChrisDusyk/FeatureFlags)
installation. Runs on a server or in a browser.

```sh
pnpm add @featureflags/client
```

ESM only, published as ES2023. Node 20.19+ / 22.12+, and any browser with `fetch` — the client
builds its request deadlines out of `AbortController` rather than the much newer `AbortSignal.any`,
so the floor stays where `fetch` put it.

## Use

```ts
import { createFeatureFlagsClient } from '@featureflags/client';

const flags = createFeatureFlagsClient({
  baseAddress: 'https://flags.example.com',
  sdkKey: process.env.FEATUREFLAGS_SDK_KEY!,
});

if (await flags.isEnabled('new-checkout')) {
  // ...
}

await flags.isEnabled('dark-mode', true); // default when the flag is unknown
```

Issue a key in the console under **Organization → Environments**. It is shown once.

**There is no environment setting.** A key is issued for one environment and carries it, so the
server decides which flags you see.

## Which key

The console asks where the key will run, and you get one of two kinds:

| | | |
|---|---|---|
| `ffs_…` | **secret** | a backend, a container, a CI job |
| `ffp_…` | **publishable** | a web or mobile app |

**In a browser you need a publishable key.** This client throws immediately if it finds a secret
key in a browser, and the server refuses one on any request carrying an `Origin` header. If you got
that far, treat the key as compromised and revoke it — anything shipped to a browser can be read
out of it.

A publishable key is public by design. Anyone who loads your app can read it, and with it every
flag key in that environment and whether each is on. Name your flags accordingly.

Your app's origin also has to be listed in the installation's `FEATUREFLAGS_BROWSER_ORIGINS`, or
the browser will refuse the response.

**The kind of key also decides how this client evaluates**, and it decides it wherever the code is
running — not just in a browser:

| | |
|---|---|
| `ffs_…` | Fetches the flag and segment definitions once per poll and evaluates them in this process. Asking about a thousand people costs a thousand map lookups. |
| `ffp_…` | Posts the context and takes the booleans back, because segment definitions cannot be shipped to a browser. Asking about a new person costs one request. |

A publishable key used server-side across many different people will make a request per person, and
that is the case that wants a secret key instead.

## Reading a flag for a particular person

A flag can be narrowed to a *segment* — a named group defined in the console from the traits your
application already knows. Describe whoever you are asking about, and the answer is about them:

```ts
const context = {
  key: user.id,
  attributes: { plan: user.plan, accountAgeDays: user.accountAgeDays, internal: user.isStaff },
};

if (await flags.isEnabled('new-checkout', context)) {
  // ...
}
```

Attributes are strings, numbers, or booleans, and comparison is exact: a condition written against
the number `30` never matches the string `'30'`, and text compares case-sensitively. Attribute
*names* are folded to lowercase, so `accountAgeDays` and `accountagedays` are the same trait.

**Calling without a context still works, and a targeted flag reads `false`.** A caller who has not
said who is asking has not described anybody a segment could contain. Nothing changes for a flag
nobody has targeted, which is every flag until somebody targets one.

For traits that describe the *process* rather than a person — the region it runs in, its tier — set
`defaultContext` once at construction. A per-call context is laid over it, so anything named at the
call site wins.

## How it behaves

**Reads do not make requests.** `isEnabled` answers from memory — a map lookup, safe on a hot path.
With a secret key that is the ruleset, refreshed on a timer every `pollingInterval` (30 seconds by
default) and on read if it has gone stale. With a publishable key it is the last answer the server
computed, held for whoever was last asked about and refetched when the context changes.

**A poll that finds nothing changed is a 304 with no body.** The client sends the previous `ETag`
back as `If-None-Match`.

**An unreachable server does not reject at your callers.** `isEnabled` falls back to the last
snapshot it managed to read, and to your default if it never read one. A flag service being briefly
unavailable should not take down everything that reads it.

To fail fast at startup instead, await a refresh yourself — that one does report:

```ts
const flags = createFeatureFlagsClient({ ... });
await flags.refresh(); // rejects if the installation cannot be read
```

**The timer never holds a Node process open** (`unref`), so a CLI still exits. Call `close()` to
stop polling explicitly; the client keeps answering from its last snapshot afterwards.

## Surviving a longer outage, or sharing one snapshot across instances

The in-memory snapshot above is lost on restart, and every instance of your application polls the
FeatureFlags server independently. If you'd rather a freshly started instance answer correctly from
its very first read, or want an outage survived for longer than one process happens to stay up,
give the client a `cache` — a small interface you implement against whatever Redis client (or other
store) your own application already uses. Nothing above changes if you don't set one; this is
additive, and there's no default implementation, because there's no Redis client this package could
import without breaking a browser bundle for everyone who never touches this option.

**This is a secret-key feature.** What it stores is the ruleset, which is the thing only an `ffs_`
client ever holds. A publishable-key client has answers about one person instead, and writing those
into a store your whole application shares is not something this package will do quietly — set
`cache` on one and it is simply unused.

```ts
import Redis from 'ioredis';
import { createFeatureFlagsClient, type FeatureFlagsCacheStore } from '@featureflags/client';

const redis = new Redis(process.env.REDIS_URL!);

const cache: FeatureFlagsCacheStore = {
  get: (key) => redis.get(key),
  set: (key, value, ttlSeconds) => redis.set(key, value, 'EX', ttlSeconds).then(() => {}),
};

const flags = createFeatureFlagsClient({
  baseAddress: 'https://flags.example.com',
  sdkKey: process.env.FEATUREFLAGS_SDK_KEY!,
  cache,
});
```

Or with [`redis`](https://www.npmjs.com/package/redis):

```ts
import { createClient } from 'redis';

const redis = await createClient({ url: process.env.REDIS_URL }).connect();

const cache: FeatureFlagsCacheStore = {
  get: (key) => redis.get(key),
  set: (key, value, ttlSeconds) => redis.set(key, value, { EX: ttlSeconds }).then(() => {}),
};
```

**Two different settings govern staleness, on purpose.** `pollingInterval` is still the normal
freshness bound — how long an answer may go before the origin is asked again. `cacheTtlSeconds`
(86400, a day, by default) is the new one: how long a value written to `cache` may still be served
once the origin is genuinely unreachable. Keep it much larger than `pollingInterval` — if the two
were close, the store would buy almost no protection over the in-memory snapshot alone.

**A cold process reads the store before ever asking the origin**, and if what it holds is still
within `pollingInterval`, that value is trusted outright with no request made at all. An older
value is still handed to the server as the conditional-request baseline, so even a stale-but-
unchanged entry costs only a 304, not a full refetch.

**A poll that finds nothing changed still refreshes the store occasionally, not on every 304.** A
long-lived process whose flags never change would otherwise let its own store entry lapse past
`cacheTtlSeconds` despite polling successfully the whole time — the entry is rewritten once it's
about half as old as `cacheTtlSeconds` allows, which keeps it from ever getting close to expiring
under a healthy client without writing on every single poll.

**A failure in your store never surfaces through `isEnabled`.** A blip in your own Redis is not the
FeatureFlags server being unreachable, and is treated as a cache miss, not a client failure.

## Options

| | | |
|---|---|---|
| `baseAddress` | — | The origin the console is on. A path is kept, so an installation served under one works; a credential, query string, or fragment is refused. Required. |
| `sdkKey` | — | Issued in the console. Required. |
| `pollingInterval` | `30000` | Upper bound, in ms, on how long a toggle takes to arrive. |
| `timeout` | `10000` | How long one refresh may take, in ms. |
| `fetch` | global | For tests, or a proxy agent. |
| `cache` | none | A `FeatureFlagsCacheStore` backed by your own Redis (or other store). Optional. |
| `cacheTtlSeconds` | `86400` | How long a value in `cache` survives a real outage. Only meaningful with `cache` set. |
| `defaultContext` | none | Traits every evaluation carries — the region this process runs in, its tier. A per-call context wins over it. |
| `cacheKeyPrefix` | `"featureflags:"` | Prefixed onto the key this client uses in `cache`, so it cannot collide with your application's own keys. The key already includes the installation's host and the SDK key's environment, so two environments — or two installations — sharing one store and the same `cacheKeyPrefix` still don't collide with each other. |

## Versioning

This package versions independently of the platform. Its compatibility surface is
`GET /api/evaluation/ruleset`, `POST /api/evaluation`, and the SDK key format — not the admin API,
which is closed to SDK keys by design.
