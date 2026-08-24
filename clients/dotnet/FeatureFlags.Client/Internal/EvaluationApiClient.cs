using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FeatureFlags.Evaluation;
using System.Threading;
using System.Threading.Tasks;

namespace FeatureFlags.Client.Internal;

/// <summary>
/// The one request this package makes: <c>GET /api/evaluation/ruleset</c>, conditionally.
///
/// <para>
/// The ruleset rather than the evaluated answers, because this package holds a secret key and can
/// therefore be trusted with the segment definitions — which is what lets it answer for a
/// particular person without a round trip. A browser client cannot be trusted with them and posts
/// its context to <c>/api/evaluation</c> instead; that is the whole of the split.
/// </para>
/// </summary>
internal sealed class EvaluationApiClient(HttpClient http)
{
    /// <summary>
    /// Relative, so it composes with whatever path the installation is served under. No leading
    /// slash for the same reason — one would discard any base path.
    /// </summary>
    private const string Path = "api/evaluation/ruleset";

    /// <summary>
    /// Fetches, sending the previous ETag if there is one. Returns null when the server answers 304
    /// — the caller already holds that answer and should keep it.
    /// </summary>
    /// <exception cref="FeatureFlagsException">The server refused or answered with nonsense.</exception>
    public async Task<FlagSnapshot?> FetchAsync(
        FlagSnapshot? current,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);

        if (current?.ETag is { Length: > 0 } etag)
        {
            // TryAddWithoutValidation: the tag is the server's own, echoed back verbatim. Parsing
            // and re-serialising it is a chance to change it, and a changed validator never matches.
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified && current is not null)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new FeatureFlagsException(
                "The FeatureFlags server rejected this SDK key. It may have been revoked, or it may " +
                "belong to a different installation.");
        }

        // Distinct from a 401, and worth its own sentence: the key is perfectly good and simply
        // cannot have this. Reporting "it may have been revoked" here would send somebody hunting
        // for a revocation that never happened.
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new FeatureFlagsException(
                "The FeatureFlags server refused this SDK key the ruleset. This package is " +
                "server-side and needs a secret ('ffs_') key; a publishable key evaluates through " +
                "the browser endpoint instead.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new FeatureFlagsException(
                $"The FeatureFlags server answered {(int)response.StatusCode} for {Path}.");
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Ruleset? ruleset;

        try
        {
            // RulesetJson.Options, not options of this package's own: the type being deserialized
            // is compiled from the same file the server serializes from, and reading it with
            // different settings than it was written with is exactly the seam that arrangement
            // exists to close.
            ruleset = JsonSerializer.Deserialize<Ruleset>(body, RulesetJson.Options);
        }
        catch (JsonException exception)
        {
            throw new FeatureFlagsException(
                "The FeatureFlags server's response could not be read. This usually means something " +
                "other than the API answered — a proxy, or a login page.",
                exception);
        }

        if (ruleset?.Environment is null)
        {
            throw new FeatureFlagsException("The FeatureFlags server's response was missing its flags.");
        }

        return new FlagSnapshot(ruleset, response.Headers.ETag?.ToString(), now);
    }
}
