# FeatureFlags.Client

Reads feature flags from a self-hosted [FeatureFlags](https://github.com/ChrisDusyk/FeatureFlags)
installation.

```sh
dotnet add package FeatureFlags.Client
```

Targets `netstandard2.0`, `net8.0`, and `net10.0` — so it works on .NET Framework 4.6.2 and up as
well as modern .NET.

## Use

```csharp
builder.Services.AddFeatureFlags(options =>
{
    options.BaseAddress = new Uri("https://flags.example.com");
    options.SdkKey = builder.Configuration["FeatureFlags:SdkKey"];
});
```

or bind a configuration section:

```csharp
builder.Services.AddFeatureFlags(builder.Configuration.GetSection("FeatureFlags"));
```

```csharp
public sealed class CheckoutService(IFeatureFlagClient flags)
{
    public async Task<IActionResult> Checkout(CancellationToken cancellationToken)
    {
        if (await flags.IsEnabledAsync("new-checkout", cancellationToken))
        {
            // ...
        }
    }
}
```

Issue an SDK key in the console under **Organization → Environments**. It is shown once.

**There is no environment setting.** A key is issued for one environment and carries it, so the
server decides which flags you see. One thing to configure, and no way for it to disagree with what
the console shows.

**It has to be a secret (`ffs_`) key.** This package is server-side, and it reads the flag and
segment definitions in order to evaluate them here rather than asking the server per user. A
publishable (`ffp_`) key is refused, both by this package's own validation and by the server.

## Reading a flag for a particular person

A flag can be narrowed to a *segment* — a named group defined in the console from the traits your
application already knows. Describe whoever you are asking about, and the answer is about them:

```csharp
var context = FlagContext.For(user.Id)
    .With("plan", user.Plan)
    .With("accountAgeDays", user.AccountAge.TotalDays)
    .With("internal", user.IsStaff);

if (await flags.IsEnabledAsync("new-checkout", context, cancellationToken))
{
    // ...
}
```

Attributes are strings, numbers, or booleans, and comparison is exact: a condition written against
the number `30` never matches the string `"30"`, and comparison of text is case-sensitive.

**Calling without a context still works, and a targeted flag reads `false`.** A caller who has not
said who is asking has not described anybody a segment could contain. Nothing changes for a flag
nobody has targeted, which is every flag until somebody targets one.

For traits that describe the *process* rather than a person — the region it runs in, its tier — set
`DefaultContext` once at registration. A per-call context is laid over it, so anything named at the
call site wins.

## How it behaves

**Reads do not make requests.** `IsEnabledAsync` answers from an in-memory copy of the ruleset — a
lookup and a handful of comparisons, safe to call on a hot path and safe to call per user. It is
refreshed in the background every `PollingInterval` (30 seconds by default), and lazily on read if
it has gone stale, which is what makes the package work outside a generic host.

**Evaluation happens here, not on the server.** The client fetches the flag states and the segment
definitions once per poll and decides for itself, so asking about a thousand users costs a thousand
dictionary lookups rather than a thousand requests. This is why the package needs a secret key: a
browser cannot be handed segment definitions, so browser clients post their context to the server
instead and get booleans back.

**A poll that finds nothing changed is a 304 with no body.** The client sends the previous `ETag`
back as `If-None-Match`.

**An unreachable server does not throw at your callers.** `IsEnabledAsync` falls back to the last
snapshot it managed to read, and to your default if it never read one. A flag service being briefly
unavailable should not take down everything that reads it. Set `ThrowOnStartupFailure` if starting
blind is worse for you than not starting, and call `RefreshAsync` when you want a failure reported.

**An unknown key is `false`** — a flag that does not exist is not one that is on. Use the
`defaultValue` overload to say otherwise.

## Options

| | | |
|---|---|---|
| `BaseAddress` | — | The origin the console is on. Required. |
| `SdkKey` | — | Issued in the console. Required. |
| `PollingInterval` | 30s | Upper bound on how long a toggle takes to arrive. |
| `Timeout` | 10s | How long one refresh may take. |
| `ThrowOnStartupFailure` | `false` | Whether an unreadable first snapshot stops the host. |
| `DefaultContext` | none | Traits every evaluation carries — the process's region, tier, cluster. A per-call context wins over it. |

## Surviving a longer outage, or sharing one snapshot across instances

The in-memory snapshot above is lost on restart, and each instance of your application polls the
FeatureFlags server independently. If you want a freshly started instance to answer correctly from
the moment it starts, or want an outage survived for longer than one process happens to stay up,
add [`FeatureFlags.Client.Redis`](https://www.nuget.org/packages/FeatureFlags.Client.Redis) — an
optional tier backed by Redis your own application already runs. Nothing above changes if you don't
add it; this package's behavior is unaffected either way.

## Versioning

This package versions independently of the platform. Its compatibility surface is
`GET /api/evaluation/ruleset`, the shape of a ruleset, and the SDK key format — not the admin API,
which is closed to SDK keys by design.
