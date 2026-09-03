using System.Threading;
using FutureFlags.Evaluation;
using System.Threading.Tasks;

namespace FutureFlags.Client;

/// <summary>
/// Reads feature flags for the environment this client's SDK key is scoped to.
///
/// <para>
/// Registered as a singleton by <c>AddFutureFlags</c>. Reads are served from an in-memory
/// ruleset, so calling <see cref="IsEnabledAsync(string, CancellationToken)"/> on a hot path costs
/// a lookup and a few comparisons rather than a request.
/// </para>
/// </summary>
public interface IFutureFlagsClient
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
    /// The context is laid over <see cref="FutureFlagsOptions.DefaultContext"/>: anything named
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
    /// A flag's full resolution for this person: the value, the variant it came from, why it was
    /// served, and an error code when there was one.
    ///
    /// <para>
    /// What <c>IsEnabledAsync</c> answers, with the reasoning attached. It exists because a bare
    /// boolean cannot tell "off in this environment" from "targeted at a segment you are not in"
    /// from "no such flag" — distinctions an OpenFeature provider has to make, and the reason
    /// <c>FutureFlagsProvider</c> can be a thin wrapper over this rather than a second evaluator.
    /// </para>
    /// <para>
    /// Like every other read here, it never throws: an unreachable service resolves as
    /// <c>PROVIDER_NOT_READY</c> rather than as an exception.
    /// </para>
    /// </summary>
    Task<FlagResolution> ResolveAsync(
        string key,
        FlagContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refetches now, rather than waiting for the polling interval. Throws if the fetch fails —
    /// unlike the background refresh, an explicit request to reload reports what happened.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
