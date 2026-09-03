# The evaluation engine

Whether a flag is on for a particular person is decided in three places: the server, when a browser
client posts a context to `POST /api/evaluation`; the .NET client, which pulls a ruleset and
evaluates in-process; and the Node client, which does one or the other depending on its key. Three
places that must never disagree, because a segment that matches in one and not another is a bug
nobody can reproduce.

Two things keep them together.


## The C# is one copy, linked into two projects

`dotnet/` is not a project. It is compiled by `<Compile Include>` from both
`FutureFlags.Domain.csproj` and `clients/dotnet/FutureFlags.Client/FutureFlags.Client.csproj`.

A project reference would be the obvious thing and it is not available: the client targets
`netstandard2.0` so that .NET Framework, Mono, and Unity consumers can use it, and `Domain` targets
`net10.0`. Linking the source is what turns "the server and the SDK agree" into something the
compiler checks rather than something a reviewer remembers.

**What that costs, and it is easy to trip over:**

- **Everything here compiles at the `netstandard2.0` floor.** No `required` members, no
  `IReadOnlySet<T>`, no `System.Buffers.Text.Base64Url`, no `RegexOptions.NonBacktracking`. The
  C# *language* version is fine — both projects set `LangVersion latest`, so collection expressions,
  switch expressions, primary constructors, and records all work. It is the *library* surface that
  is old.
- **Every file carries its own `using` directives.** `FutureFlags.Client` sets
  `ImplicitUsings=disable`, so a file that relies on `Domain`'s implicit usings compiles in one
  project and not the other.
- **Nothing here may touch `Result` or `Option`.** Those are `Domain`'s, and shipping them inside a
  client package would put two copies of the same railway types in one consumer's application. The
  validating, `Result`-returning wrappers live in `FutureFlags.Domain/Segments/` and call into these
  types; these types answer with plain `bool`.
- **Everything here is public API of a NuGet package.** `FutureFlags.Client` sets
  `GenerateDocumentationFile` and `EnablePackageValidation`, so a new public member needs an XML
  comment, and removing one is a breaking change for a consumer.
- **`FutureFlags.Server/Dockerfile` copies this directory explicitly.** It is not inside any
  project folder, so nothing else brings it in. Restore does not need it and `dotnet publish` does —
  the same trap `Directory.Packages.props` sets, where the solution build stays green and the image
  build fails.
- **Both CI workflows list `shared/**` in their path filters.** Without that, a change in here fires
  neither `server-ci` nor `clients-ci`, which is precisely the change that most needs testing.

## The Node client is checked against vectors

There is no way to share source with TypeScript, so `conformance/` holds the cases all three engines
must answer identically: `segments.json` for "is this context in this segment", `flags.json` for a
whole ruleset.

The `segment`, `ruleset`, and `context` members of every case are **the exact wire shapes**, byte
for byte what `GET /api/evaluation/ruleset` returns and what `POST /api/evaluation` accepts. Each
suite reads them with its *production* parser, never a test-only one — so the vectors police the
parsers as much as the engine. If a case ever needs a shape the wire does not have, the design is
wrong rather than the file.

They are run by:

- `FutureFlags.Domain.Tests/Evaluation/` — the server's compilation,
- `clients/dotnet/FutureFlags.Client.Tests/` — the `netstandard2.0`/`net8.0`/`net10.0` compilations
  of the very same source, which is really a check that the three targets agree with each other,
- `clients/node/test/conformance.test.ts` — the one genuinely independent implementation, and the
  reason these files are JSON instead of a C# theory.

**Adding a case means adding it to the JSON, not to a suite.** All three pick it up.

## Resolutions, and the reason mapping

`FlagEvaluator.Resolve` is the entry point; `Evaluate` reads its answer as a boolean. It returns a
`FlagResolution` — OpenFeature's `resolution details`, with its field names — so a provider can hand
the fields straight across rather than inventing a reason from a bare boolean, which is what the
.NET and Node clients would otherwise each have to do differently from the same data.

Which reason goes with which situation is **not a detail the three implementations may each decide**.
It is pinned by `conformance/flags.json`:

| Situation | Value | Variant | Reason | Error code |
|---|---|---|---|---|
| Flag not in the ruleset | `false` | — | `ERROR` | `FLAG_NOT_FOUND` |
| Off in this environment | off variant | `off` | `DISABLED` | — |
| On, targeting nobody | on variant | `on` | `STATIC` | — |
| On, a targeted segment matched | on variant | `on` | `TARGETING_MATCH` | — |
| On, targeting, matched nothing | off variant | `off` | `DEFAULT` | — |

The last row is the one to read twice. It is a **normal** answer with **no error code**: the flag
exists, the ruleset is intact, and the subject is simply not in the segment. Calling it an error
would be wrong on the facts and expensive in practice — consumers alert on error codes, so every
deliberately narrowed flag would look like an outage. The vectors assert `errorCode` explicitly,
including its absence, because a reason-only assertion would not catch a regression that started
setting one alongside `DEFAULT`.

Booleans are unchanged by all of this, including the documented behaviour that `GET /api/evaluation`
reads a targeted flag as `false`. Only the explanation is new.

## Conformance vector schema (version 2)

`flags.json` case `expected` is a map of flag key to `{ value, variant, reason }`, with an optional
`errorCode` that no normal case carries. A case may also name `missing` keys — ones the ruleset
deliberately does not carry — which every runner resolves individually and asserts as
`ERROR`/`FLAG_NOT_FOUND`, since a whole-ruleset answer can never contain a key it has never heard
of. `segments.json` is unchanged.

Three suites read these: `FutureFlags.Domain.Tests`, `FutureFlags.Client.Tests`, and
`clients/node/test/conformance.test.ts`. Add a case and all three pick it up.

## What is deliberately not here

There is no regular-expression operator. It is safe on the server
(`RegexOptions.NonBacktracking` gives linear time) and cannot be made safe in the browser, which has
neither a match timeout nor a linear-time engine. Validating patterns server-side does not rescue
it: the canonical catastrophic pattern `(a+)+b` uses no lookaround and no backreference and compiles
happily under `NonBacktracking`. An operator that is linear in two engines and a hang in the third
is worse than a missing one, and `contains` / `starts-with` / `ends-with` / `one-of` cover most of
what it would have been used for.
