using System;

namespace FutureFlags.Evaluation;

/// <summary>
/// Why an evaluation answered what it did, in OpenFeature's vocabulary.
///
/// <para>
/// Plain strings rather than an enum, for two reasons. The OpenFeature specification allows a
/// provider to return a reason of its own devising, so a closed enum would make an unrecognised
/// value unrepresentable at exactly the moment a client most needs to pass it through untouched.
/// And this file is compiled into the client package, where turning an unknown wire string into a
/// thrown exception would break the one rule an OpenFeature client must never break: never throw.
/// </para>
/// <para>
/// Which reason this build produces for which situation is fixed and is part of the conformance
/// vectors — see <c>shared/evaluation/conformance/flags.json</c>. The mapping is not a detail the
/// three implementations may each decide for themselves.
/// </para>
/// </summary>
public static class EvaluationReason
{
    /// <summary>Resolved from a static configuration with no targeting involved. This build uses it
    /// for a flag that is on and targets nobody — on for everyone.</summary>
    public const string Static = "STATIC";

    /// <summary>Resolved to the flag's default variant. This build uses it for a flag that is on
    /// and targets segments, none of which the context is in.</summary>
    public const string Default = "DEFAULT";

    /// <summary>Resolved because the context matched a targeting rule.</summary>
    public const string TargetingMatch = "TARGETING_MATCH";

    /// <summary>Resolved by a pseudorandom split. Not produced by this build; percentage rollout is
    /// a later feature.</summary>
    public const string Split = "SPLIT";

    /// <summary>Served from a cache.</summary>
    public const string Cached = "CACHED";

    /// <summary>The flag is disabled in this environment, so its off variant was served.</summary>
    public const string Disabled = "DISABLED";

    /// <summary>The reason is not known.</summary>
    public const string Unknown = "UNKNOWN";

    /// <summary>Served from a snapshot known to be out of date.</summary>
    public const string Stale = "STALE";

    /// <summary>An error occurred and the default was served. Always accompanied by an error
    /// code — see <see cref="EvaluationErrorCode"/>.</summary>
    public const string Error = "ERROR";
}
