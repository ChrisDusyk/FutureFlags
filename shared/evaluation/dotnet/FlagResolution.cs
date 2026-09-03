using System;
using System.Collections.Generic;

namespace FutureFlags.Evaluation;

/// <summary>
/// One flag's answer for one context, with the reasoning attached.
///
/// <para>
/// This is OpenFeature's <c>resolution details</c> structure, and the field names are its names on
/// purpose. A provider built on this can hand the fields straight across rather than inventing a
/// reason from a bare boolean — which is what the .NET and Node clients would otherwise each have
/// to do, differently, from the same data.
/// </para>
/// <para>
/// <see cref="FlagMetadata"/> is an empty dictionary and never null, per the specification's
/// requirement that the field contain an empty record when a provider sets nothing. Callers may
/// read it without a guard.
/// </para>
/// </summary>
/// <param name="value">The value served.</param>
/// <param name="variant">The name of the variant it came from, if there is one.</param>
/// <param name="reason">Why this value was served. One of <see cref="EvaluationReason"/>.</param>
/// <param name="errorCode">The error, if this was an abnormal resolution. One of
/// <see cref="EvaluationErrorCode"/>.</param>
/// <param name="errorMessage">Detail about the error, if there is any worth carrying.</param>
/// <param name="flagMetadata">Arbitrary provider metadata. Null becomes an empty record.</param>
public sealed class FlagResolution(
    FlagValue value,
    string? variant,
    string reason,
    string? errorCode = null,
    string? errorMessage = null,
    IReadOnlyDictionary<string, AttributeValue>? flagMetadata = null)
{
    private static readonly IReadOnlyDictionary<string, AttributeValue> NoMetadata =
        new Dictionary<string, AttributeValue>(StringComparer.Ordinal);

    /// <summary>The value served.</summary>
    public FlagValue Value { get; } = value;

    /// <summary>The name of the variant the value came from, or null when no variant applies —
    /// which in this build means the flag was not found at all.</summary>
    public string? Variant { get; } = variant;

    /// <summary>Why this value was served. One of the <see cref="EvaluationReason"/> constants, but
    /// typed as a string because a reason from elsewhere must survive being passed through.</summary>
    public string Reason { get; } = reason;

    /// <summary>The error, when <see cref="Reason"/> is <see cref="EvaluationReason.Error"/>, and
    /// null otherwise. Never populated on a normal resolution.</summary>
    public string? ErrorCode { get; } = errorCode;

    /// <summary>Detail about the error, when there is any worth carrying.</summary>
    public string? ErrorMessage { get; } = errorMessage;

    /// <summary>Arbitrary provider metadata. Empty, never null.</summary>
    public IReadOnlyDictionary<string, AttributeValue> FlagMetadata { get; } = flagMetadata ?? NoMetadata;

    /// <summary>
    /// Whether this resolution succeeded. Note that a false value is not a failure — a disabled
    /// flag and an unmatched targeting rule both resolve normally.
    /// </summary>
    public bool IsSuccess => ErrorCode is null;

    /// <summary>
    /// The boolean this resolution carries, or <paramref name="defaultValue"/> when it carries
    /// something else or nothing at all. This is the bridge every boolean-typed caller crosses, and
    /// it is why a type mismatch can never throw.
    /// </summary>
    public bool AsBoolean(bool defaultValue = false) =>
        IsSuccess && Value.Kind == FlagValueKind.Boolean ? Value.Boolean : defaultValue;
}
