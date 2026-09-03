using System;

namespace FutureFlags.Evaluation;

/// <summary>
/// Why an evaluation failed, in OpenFeature's vocabulary.
///
/// <para>
/// Strings rather than an enum, for the reasons given on <see cref="EvaluationReason"/>.
/// </para>
/// <para>
/// An error code is populated only alongside <see cref="EvaluationReason.Error"/>, and never
/// otherwise. That pairing matters more than it looks: consumers routinely alert on an error code
/// being present at all, so populating one on a normal answer turns ordinary behaviour into a page.
/// This is why a flag that is targeted and simply did not match answers
/// <see cref="EvaluationReason.Default"/> with no error code — it succeeded, and the subject is
/// merely not in the segment.
/// </para>
/// </summary>
public static class EvaluationErrorCode
{
    /// <summary>The provider has not finished initialising.</summary>
    public const string ProviderNotReady = "PROVIDER_NOT_READY";

    /// <summary>No flag by that key exists in this environment.</summary>
    public const string FlagNotFound = "FLAG_NOT_FOUND";

    /// <summary>The flag's configuration could not be parsed.</summary>
    public const string ParseError = "PARSE_ERROR";

    /// <summary>The flag exists but does not hold the type the caller asked for. This build returns
    /// it for every non-boolean resolution, because every flag is boolean.</summary>
    public const string TypeMismatch = "TYPE_MISMATCH";

    /// <summary>The evaluation context carried no targeting key and one was required.</summary>
    public const string TargetingKeyMissing = "TARGETING_KEY_MISSING";

    /// <summary>The evaluation context was not usable.</summary>
    public const string InvalidContext = "INVALID_CONTEXT";

    /// <summary>The provider has failed irrecoverably and should not be retried.</summary>
    public const string ProviderFatal = "PROVIDER_FATAL";

    /// <summary>Anything else.</summary>
    public const string General = "GENERAL";
}
