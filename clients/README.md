# Client libraries

Where the NuGet and npm packages live:

```
clients/dotnet/    FutureFlags.Client         → NuGet
clients/dotnet/    FutureFlags.Client.Redis   → NuGet (optional Redis cache tier, see below)
clients/node/      @futureflags/client        → npm
```

The release plumbing is in `.github/workflows/sdk-release.yml`, on the tag prefixes
`sdk-dotnet-v*` and `sdk-node-v*`. The OpenAPI document is produced at build time and attached to
every platform release. `.github/workflows/clients-ci.yml` is the pull-request side of the same
pair — both packages built, tested, and linted on any change under `clients/`. The .NET job builds
the library on its own rather than through its tests, because it multi-targets and a
`ProjectReference` from the `net10.0` test project only ever builds the `net10.0` one.

**`FutureFlags.Client.Redis` is the one exception to "each package versions on its own tag."** It
ships on `sdk-dotnet-v*` alongside `FutureFlags.Client` rather than a tag of its own, because it
references the base package by project reference — a version bump in one is a version bump in both,
and a separate tag scheme would mean releasing it every time the base package moved for a reason
the add-on had no part in. It is optional either way: the base package has no dependency on it, and
nothing about `FutureFlags.Client`'s own behavior changes if it is never installed.

## What a client talks to

Three things had to land in the platform first, and all three have:

1. **SDK keys.** A credential a program can hold: issued per environment from the console, revocable,
   and presented as a bearer token the server recognises without a user behind it. Before this the
   only credential the system issued was a fifteen-minute user JWT minted from a browser session
   cookie — nothing a server-side SDK could hold without impersonating the console.
2. **An evaluation endpoint.** `GET /api/evaluation` answers with the flag states for the key's
   own environment and an ETag, so a poll that finds nothing changed costs a 304. Before this the
   only read was the admin listing, with console metadata, no ETag, and no server-side caching.
3. **Segments, and with them two more routes.** A flag can be narrowed to a named group defined
   from the traits an application already knows, which means a client has to be able to say *who*
   it is asking about. See "Two kinds of key" below — how it asks depends on which kind it holds.

A client therefore needs exactly two settings — the origin and the key. The environment is not
configurable because the key carries it, and neither is the transport: the key decides that too.

**A client's own cache settings and the server's are two different things that happen to rhyme.**
Every evaluation route is served from a ruleset cached server-side for 5 seconds (`HybridCache`,
see `RulesetProvider`), which is what makes a client's own polling cheap rather than a bulk read
against the database on every request. Polling more often than that 5-second server-side TTL gains
nothing — a poll that lands inside it still costs a round trip, even though the body is empty on a
304. And on the client's own Redis cache tier (`FutureFlags.Client.Redis`'s `FailSafeMaxDuration`,
or the Node client's `cacheTtlSeconds`): that number extends how long a client *survives an outage*
of the FutureFlags server. It does not change how quickly a client *sees a change* under normal
operation — that is still bounded by `PollingInterval`/`pollingInterval` alone. "We added caching"
reads easily as "flags update slower now," and the two client caches here do not do that; they only
change what happens when the origin cannot be reached at all.

## Two kinds of key

`ffs_` keys are secret and server-side only. `ffp_` keys are publishable and may be shipped to a
browser. The kind decides where a key may be used *from* — a request carrying an `Origin` header
must present a publishable key, and the server enforces that.

**Since segments, the kind also decides what a key may read, and therefore where evaluation
happens.** A segment definition can name people, and a publishable key is expected to be readable
by anyone who can open a bundle, so the two cannot meet:

| | | |
|---|---|---|
| `ffs_` | `GET /api/evaluation/ruleset` | The flag states *and* the segment definitions. The client evaluates in-process, so asking about a thousand people costs no requests at all. |
| `ffp_` | `POST /ofrep/v1/evaluate/flags` | The context goes up, values come back. Definitions never leave the server. One request per distinct context. |

A client picks its transport from its key prefix, not from whether it is running in a browser: a
publishable key used server-side still cannot have the ruleset, because the server will not give it
one — it answers 403 with a body saying which route to use instead. The .NET client is secret-only
and has only the first path; the Node client runs in both places and has both.

`GET /api/evaluation` is unchanged and still answers key-to-boolean for nobody in particular. A
flag that has been narrowed to a segment reads `false` there, which is the safe direction: a caller
that has never been told who is asking has not described anybody a segment could contain.

## OpenFeature

FutureFlags speaks the [OpenFeature Remote Evaluation Protocol](https://openfeature.dev), which is
the point: **any OpenFeature SDK, in any language, reaches a FutureFlags server through its stock
OFREP provider, with no FutureFlags-specific code at all.**

| | |
|---|---|
| `POST /ofrep/v1/evaluate/flags` | Every flag for one context. Conditional — it sets an `ETag` and honours `If-None-Match`. |
| `POST /ofrep/v1/evaluate/flags/{key}` | One flag. A key the environment does not carry is a 404 with `errorCode: "FLAG_NOT_FOUND"`. |

Either kind of SDK key works on both: they answer with values, never with definitions, so there is
nothing there a publishable key must not see. Point an OFREP provider at the origin with the key as
a bearer token and it works.

The clients here also ship OpenFeature providers, so you can use the OpenFeature API instead of
theirs:

- **.NET** — `FutureFlags.Client.OpenFeature.FutureFlagsProvider`, in the same package. Evaluates
  in-process from the ruleset, so it needs an `ffs_` key.
- **Node, server-side** — `@futureflags/client/openfeature/server`, implementing
  `@openfeature/server-sdk`. Dynamic context, `ffs_` key, in-process.
- **Node, browser-side** — `@futureflags/client/openfeature/web`, implementing
  `@openfeature/web-sdk`. Static context, `ffp_` key, reads the OFREP route and refetches when the
  context changes.

Both OpenFeature SDKs are optional peer dependencies of the Node package; the root import pulls in
neither.

### What conformance does and does not cover

**Every flag is boolean.** `getBooleanValue` works. `getStringValue`, `getNumberValue` and
`getObjectValue` return your own default with `TYPE_MISMATCH` — honestly, rather than coercing
something out of a boolean. The wire already carries a value type and a variant set, so the other
three types are a domain change rather than another protocol change.

**Reasons are real, and one of them is worth knowing.** A flag that is off here reports `DISABLED`;
on and targeting nobody reports `STATIC`; on and matched reports `TARGETING_MATCH`. A flag that is
on, targets segments, and matched *none* of them reports **`DEFAULT` with no error code**. It is a
normal answer — the flag exists and resolved to its default variant, and the subject is simply not
in the segment. Reporting it as an error would make every deliberately narrowed flag look like an
outage to anything alerting on error codes.

**Context values are text, numbers and booleans.** OpenFeature's context permits nested structures,
lists and datetimes. A value this platform cannot hold is *dropped* rather than rejected, which is
the same reading it already gives an unset attribute: absent, and absent never matches. Failing
instead would mean one unrelated object in a context stops every flag resolving. A datetime becomes
ISO-8601 text, which is the only form three runtimes compare the same way. `targetingKey` is the
context key; `key` is accepted as an alias.

**Deprecated, not removed.** `GET /api/evaluation` and `POST /api/evaluation` still answer exactly
as they always have, and now send a `Deprecation` header. They are marked deprecated in the OpenAPI
document. `GET /api/evaluation/ruleset` is **not** deprecated: OFREP defines only single and bulk
evaluation, both of which return values, so it has no equivalent of shipping a ruleset for
in-process evaluation — which is what the `ffs_` key kind exists for.

The node client refuses to start with a secret key in a browser, so that mistake surfaces at the
line that configured it rather than as a 401 after the key is already in a bundle. The .NET client
is server-side only and has no such check to make.

The admin endpoints (`GET /api/flags?environment=`, `POST /api/flags`, `PUT /api/flags/{key}/state`,
`PUT /api/flags/{key}/targeting`, `GET /api/users/me`, the `segments` routes, the `sdk-keys` routes)
are **closed to SDK keys**. They are the console's, they
require a user's token, and a client library has no business calling them. A management client for
automation is a separate thing from a feature-flag SDK, and would need a credential that does not
exist yet.

## Versioning

Client libraries version independently of the platform, against a documented minimum server
version. An SDK churns on a different clock than the thing it talks to, and tying them together
would mean publishing a no-op package release on every platform bump.

`GET /api/evaluation`, `GET /api/evaluation/ruleset`, `POST /api/evaluation`, the two
`/ofrep/v1/evaluate/flags` routes, the shape of a ruleset, and the SDK key format are the
compatibility surface. The minimum server version for any client published from here is the first
release that carries them.

A ruleset grows fields additively: `key`, `isEnabled` and `targetedSegments` keep their names and
meanings, so a released SDK reads a newer server's ruleset and a newer SDK reads an older server's
as boolean flags with the standard variant pair. The browser-side OpenFeature provider is the one
exception to "any server": it reads the OFREP route, so it needs a server that has one.

**The evaluation rule itself is pinned by `shared/evaluation/conformance/*.json`.** The server and
the .NET client compile one evaluator from one shared C# source; the Node client is a separate
implementation, and those vectors are what hold it to the same answers. A change to the rule that
is not also made there fails three suites.
