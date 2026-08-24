using System;
using System.Threading;
using System.Threading.Tasks;
using FeatureFlags.Client.Internal;
using FeatureFlags.Client.Redis.Internal;
using FeatureFlags.Evaluation;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace FeatureFlags.Client.Redis;

/// <summary>
/// An <see cref="IFeatureFlagClient"/> backed by <see cref="IFusionCache"/>: an in-memory tier the
/// same as the base client's, and a Redis tier behind it that survives a process restart and is
/// shared across every instance of the host application pointed at the same Redis.
///
/// <para>
/// This does not wrap <see cref="FeatureFlagClient"/> — it talks to <see cref="EvaluationApiClient"/>
/// directly, because caching the whole flag set as one entry (matching the server's own
/// <c>evaluation:{environment}</c> granularity) needs the snapshot itself, which
/// <see cref="IFeatureFlagClient"/>'s per-flag surface does not expose. <see cref="FetchAsync"/> is
/// FusionCache's factory: it reuses the same conditional-GET logic the base client's polling loop
/// does, so a healthy read still costs a 304 with no body, not a full refetch every time the cache
/// tier decides to check.
/// </para>
/// </summary>
internal sealed class RedisCachedFeatureFlagClient : IFeatureFlagClient
{
    private readonly EvaluationApiClient _api;
    private readonly IFusionCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly FusionCacheEntryOptions _entryOptions;
    private readonly string _cacheKey;
    private readonly FlagContext? _defaultContext;

    private static readonly Ruleset EmptyRuleset = new(string.Empty, [], []);

    public RedisCachedFeatureFlagClient(
        EvaluationApiClient api,
        IFusionCache cache,
        IOptions<FeatureFlagsOptions> options,
        FeatureFlagsRedisCacheOptions redisOptions,
        TimeProvider timeProvider)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (redisOptions is null)
        {
            throw new ArgumentNullException(nameof(redisOptions));
        }

        // Environment and host, not just KeyPrefix: two environments (or two installations) sharing
        // one Redis would otherwise overwrite each other's snapshot under the same key. The
        // environment segment of an SDK key is documentation, never trusted for authorization — the
        // server's own claim decides what a key can read — but namespacing a cache key by it is a
        // different, lower-stakes use that this is fine for.
        _cacheKey = BuildCacheKey(redisOptions.KeyPrefix, options.Value);
        _defaultContext = options.Value.DefaultContext;

        // Duration mirrors PollingInterval on purpose: it is the same "how stale may this get
        // before asking again" contract the base client already documents, just enforced by
        // FusionCache instead of FeatureFlagsRefreshService. FailSafeMaxDuration is the number that
        // is actually new here — how long a stale answer survives a real outage — and it is
        // deliberately a different order of magnitude, configured separately.
        _entryOptions = new FusionCacheEntryOptions
        {
            Duration = options.Value.PollingInterval,
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = redisOptions.FailSafeMaxDuration,
            FailSafeThrottleDuration = redisOptions.FailSafeThrottleDuration,
            EagerRefreshThreshold = redisOptions.EagerRefreshThreshold
        };
    }

    public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) =>
        IsEnabledAsync(key, FlagContext.Empty, defaultValue: false, cancellationToken);

    public Task<bool> IsEnabledAsync(
        string key,
        bool defaultValue,
        CancellationToken cancellationToken = default) =>
        IsEnabledAsync(key, FlagContext.Empty, defaultValue, cancellationToken);

    public Task<bool> IsEnabledAsync(
        string key,
        FlagContext context,
        CancellationToken cancellationToken = default) =>
        IsEnabledAsync(key, context, defaultValue: false, cancellationToken);

    public async Task<bool> IsEnabledAsync(
        string key,
        FlagContext context,
        bool defaultValue,
        CancellationToken cancellationToken = default)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        CachedFlags? cached;

        try
        {
            // A lambda, not the FetchAsync method group, and TValue spelled out: GetOrSetAsync is
            // overloaded with a plain TValue defaultValue parameter in the same position, and
            // neither a method group nor a lambda gives the compiler enough to infer TValue from
            // and pick the factory overload on its own.
            cached = await _cache
                .GetOrSetAsync<CachedFlags>(
                    _cacheKey,
                    (ctx, ct) => FetchAsync(ctx, ct),
                    options: _entryOptions,
                    token: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // The origin could not be reached, and Redis had nothing usable either — either it has
        // never been populated, or the outage has outlasted FailSafeMaxDuration. The caller's
        // default beats an exception, the same contract IFeatureFlagClient makes everywhere else.
        catch (Exception)
        {
            return defaultValue;
        }

        // The same reader the base client uses, so a Redis tier in front cannot change the answer
        // — only where the ruleset it answers from came from.
        return RulesetReader.IsEnabled(cached?.Ruleset, key, context, _defaultContext, defaultValue);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Unconditional: RefreshAsync means "get the latest now," and a caller who asked for that
        // explicitly should not skip the network on the strength of an ETag that might be stale for
        // a reason they are trying to rule out.
        var fetched = await _api.FetchAsync(current: null, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        // current was null above, so the 304 branch in EvaluationApiClient.FetchAsync cannot be
        // taken — every call here returns a real snapshot or throws.
        var cached = ToCached(fetched!);

        // Writes through both tiers, and — with a backplane configured — publishes to every other
        // instance sharing this Redis, so an explicit refresh here is not just local.
        await _cache.SetAsync(_cacheKey, cached, _entryOptions, token: cancellationToken).ConfigureAwait(false);
    }

    private async Task<CachedFlags> FetchAsync(
        FusionCacheFactoryExecutionContext<CachedFlags> context,
        CancellationToken cancellationToken)
    {
        // FusionCache remembers the ETag from the last Modified() call for this key; a synthetic
        // snapshot is the cheapest way to hand it to EvaluationApiClient, which only ever reads
        // current?.ETag back out of it.
        var current = context.HasETag
            ? new FlagSnapshot(EmptyRuleset, context.ETag, default)
            : null;

        var fetched = await _api.FetchAsync(current, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        if (fetched is null)
        {
            // 304: the entry FusionCache already holds is still correct, just older than Duration
            // allowed. NotModified() re-stamps it without a write, the same thing RefreshedAt does
            // in the base client for the same reason.
            return context.NotModified();
        }

        return context.Modified(ToCached(fetched), fetched.ETag);
    }

    private static CachedFlags ToCached(FlagSnapshot snapshot) =>
        new(snapshot.Ruleset, snapshot.ETag);

    private static string BuildCacheKey(string keyPrefix, FeatureFlagsOptions options)
    {
        var host = FormatHost(options.BaseAddress);

        // Versioned, because the cached payload used to be a map of answers and is now a ruleset.
        // Without it, a rolling upgrade would have a new instance deserializing an old instance's
        // entry as the wrong shape — for as long as FailSafeMaxDuration lets it survive.
        return $"{keyPrefix}{host}:{ParseEnvironment(options.SdkKey)}:ruleset:v1";
    }

    // Uri.Host alone drops the port, which would collide two installations that differ only by
    // port — the common shape for two local instances on the same machine. Uri.Port is included
    // only when it isn't the scheme's default, matching what the Node client's URL.host does.
    private static string FormatHost(Uri? baseAddress) =>
        baseAddress switch
        {
            null => "unknown",
            { IsDefaultPort: true } => baseAddress.Host,
            _ => $"{baseAddress.Host}:{baseAddress.Port}"
        };

    /// <summary>
    /// The environment segment of an SDK key (<c>ffs_{env}_{selector}_{secret}</c>), used only to
    /// namespace this cache key. "unknown" for anything that does not look like that shape — a
    /// wrong namespace is a cache miss, not a wrong answer, so this does not need to duplicate the
    /// base package's own key-format validation.
    /// </summary>
    private static string ParseEnvironment(string? sdkKey)
    {
        var segments = sdkKey?.Split('_');

        return segments is { Length: > 1 } && segments[1].Length > 0 ? segments[1] : "unknown";
    }
}
