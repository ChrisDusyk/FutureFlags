using System;
using System.Threading;
using System.Threading.Tasks;
using FeatureFlags.Client.Internal;
using FeatureFlags.Evaluation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FeatureFlags.Client;

/// <summary>
/// The default <see cref="IFeatureFlagClient"/>: an in-memory snapshot, refreshed behind the reads.
///
/// <para>
/// A read never waits on the network if it can help it. The snapshot is a single immutable
/// reference, so <see cref="IsEnabledAsync(string, CancellationToken)"/> is a field read and an
/// in-process evaluation — no lock, and nothing that can be observed half-updated.
/// </para>
///
/// <para>
/// Refreshing happens two ways, and both matter. <c>FeatureFlagsRefreshService</c> primes the
/// snapshot at startup and polls, which is what a hosted application gets. The lazy path below
/// covers everything else: a console application, a .NET Framework service, a test — anywhere
/// there is no host to run a background service at all.
/// </para>
/// </summary>
internal sealed class FeatureFlagClient(
    EvaluationApiClient api,
    IOptions<FeatureFlagsOptions> options,
    ILogger<FeatureFlagClient> logger,
    TimeProvider timeProvider) : IFeatureFlagClient, IDisposable
{
    private readonly FeatureFlagsOptions _options = options.Value;

    // One refresh at a time. Twenty threads finding the snapshot stale at once should produce one
    // request, and the nineteen that lost should use what the winner fetched.
    private readonly SemaphoreSlim _refreshing = new(1, 1);

    private volatile FlagSnapshot? _snapshot;

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

        var snapshot = await CurrentAsync(cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
        {
            // Nothing has ever loaded. Answering with the default beats throwing: a flag service
            // that cannot be reached should not be able to take down everything that reads it.
            return defaultValue;
        }

        return RulesetReader.IsEnabled(
            snapshot.Ruleset, key, context, _options.DefaultContext, defaultValue);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(throwOnFailure: true, cancellationToken);

    /// <summary>
    /// The snapshot to answer from, refreshing first if it is missing or older than the polling
    /// interval. A failure here is logged and swallowed — the caller wants an answer, and the last
    /// good one is a better answer than an exception.
    /// </summary>
    private async Task<FlagSnapshot?> CurrentAsync(CancellationToken cancellationToken)
    {
        var snapshot = _snapshot;

        if (snapshot is not null && timeProvider.GetUtcNow() - snapshot.FetchedAt < _options.PollingInterval)
        {
            return snapshot;
        }

        await RefreshAsync(throwOnFailure: false, cancellationToken).ConfigureAwait(false);

        return _snapshot;
    }

    internal async Task RefreshAsync(bool throwOnFailure, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var entered = _snapshot;

        await _refreshing.WaitAsync(linked.Token).ConfigureAwait(false);

        try
        {
            // Somebody else refreshed while this call queued. Their answer is as good as the one
            // this would fetch, and a second request for it would be waste.
            if (!ReferenceEquals(_snapshot, entered) && _snapshot is not null)
            {
                return;
            }

            var current = _snapshot;
            var fetched = await api.FetchAsync(current, timeProvider.GetUtcNow(), linked.Token)
                .ConfigureAwait(false);

            // Null is a 304: the answer is unchanged, so only its age moves. Without re-stamping,
            // an unchanged snapshot would look stale forever and be refetched on every read.
            _snapshot = fetched ?? current?.RefreshedAt(timeProvider.GetUtcNow());
        }
        // The caller asked to stop. That is not a failure to absorb — it is the caller's own
        // instruction, and swallowing it would leave them waiting on a token they already cancelled.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // Everything else, including a timeout. A timeout arrives as an OperationCanceledException
        // too, from the linked source above rather than from the caller — telling the two apart is
        // the whole point of the filter, because a server that accepts a connection and then never
        // answers must fall back like any other unreachable one rather than throw at a reader.
        catch (Exception exception) when (!throwOnFailure)
        {
            logger.LogWarning(
                exception,
                "Could not refresh feature flags. Continuing with the flags last read{Age}.",
                _snapshot is null ? " — none yet, so callers get their defaults" : string.Empty);
        }
        finally
        {
            _refreshing.Release();
        }
    }

    public void Dispose() => _refreshing.Dispose();
}
