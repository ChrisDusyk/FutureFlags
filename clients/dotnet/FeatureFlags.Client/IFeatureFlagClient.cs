using System.Threading;
using FeatureFlags.Evaluation;
using System.Threading.Tasks;

namespace FeatureFlags.Client;

/// <summary>
/// Reads feature flags for the environment this client's SDK key is scoped to.
///
/// <para>
/// Registered as a singleton by <c>AddFeatureFlags</c>. Reads are served from an in-memory
/// ruleset, so calling <see cref="IsEnabledAsync(string, CancellationToken)"/> on a hot path costs
/// a lookup and a few comparisons rather than a request.
/// </para>
/// </summary>
public interface IFeatureFlagClient
{
    /// <summary>
    /// Whether a flag is on, for nobody in particular. A key this installation has never heard of
    /// is <c>false</c>: a flag that does not exist is not one that is on.
    ///
    /// <para>
    /// A flag narrowed to a segment is <c>false</c> here, because a caller who has not said who is
    /// asking has not described anybody the segment could contain. Pass an
    /// <see cref="FlagContext"/> to get an answer about a person.
    /// </para>
    /// </summary>
    Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a flag is on, with the answer to give when there is nothing to go on — an unknown
    /// key, or a snapshot that has never loaded because the service could not be reached.
    /// </summary>
    Task<bool> IsEnabledAsync(string key, bool defaultValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a flag is on for this person.
    ///
    /// <para>
    /// Evaluated in this process against the ruleset last fetched, so it stays a dictionary lookup
    /// and a handful of comparisons rather than a request — which is what makes it safe to call per
    /// user, per request, on a hot path.
    /// </para>
    /// <para>
    /// The context is laid over <see cref="FeatureFlagsOptions.DefaultContext"/>: anything named
    /// here wins, and traits that never change can be set once at registration instead of at every
    /// call site.
    /// </para>
    /// </summary>
    Task<bool> IsEnabledAsync(string key, FlagContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a flag is on for this person, with the answer to give when there is nothing to go on.
    /// </summary>
    Task<bool> IsEnabledAsync(
        string key,
        FlagContext context,
        bool defaultValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refetches now, rather than waiting for the polling interval. Throws if the fetch fails —
    /// unlike the background refresh, an explicit request to reload reports what happened.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
