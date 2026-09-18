using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FutureFlags.Domain.Shared;
using FutureFlags.Evaluation;

namespace FutureFlags.Server.Evaluation;

/// <summary>
/// The shapes and the context binding the two OFREP routes share.
///
/// <para>
/// Here rather than in either slice, on the same terms as <see cref="RulesetProvider"/> and
/// <see cref="ConditionalResponse"/>: the OpenFeature Remote Evaluation Protocol defines one
/// success shape used by both its endpoints, and two copies of it would be two chances to answer a
/// vendor-neutral client something it cannot read.
/// </para>
/// <para>
/// The protocol's context is flat — <c>targetingKey</c> alongside arbitrary custom fields — where
/// this platform's own routes nest attributes under <c>attributes</c>. That difference is the whole
/// reason this binder exists separately from <c>EvaluateForContextEndpoint.Bind</c>.
/// </para>
/// </summary>
public static class Ofrep
{
    /// <summary>The field OpenFeature reserves for the subject of an evaluation.</summary>
    public const string TargetingKey = "targetingKey";

    /// <summary>
    /// The alias this platform's own routes use. Accepted so a caller that already speaks
    /// FutureFlags can point at these routes without rewriting its context, and because a context
    /// carrying only <c>key</c> would otherwise silently evaluate as anonymous — the failure mode
    /// where every segment stops matching and nothing says why.
    /// </summary>
    public const string TargetingKeyAlias = "key";

    /// <summary>
    /// Caps on the one route where an authenticated caller decides how much work the server does,
    /// matching <c>POST /api/evaluation</c>'s exactly — a context is a context whichever protocol
    /// carried it, and two different limits would be two different answers to the same question.
    /// </summary>
    public const int MaxAttributes = 64;
    public const int MaxAttributeNameLength = 100;
    public const int MaxContextKeyLength = 256;
    public const int MaxRequestBytes = 16 * 1024;

    /// <summary>
    /// Reads an OFREP evaluation context into a <see cref="FlagContext"/>.
    ///
    /// <para>
    /// OpenFeature's context permits nested structures, lists, and datetimes;
    /// <see cref="AttributeValue"/> holds only text, numbers, and booleans. An unrepresentable
    /// value is <em>dropped</em> rather than failing the request, which is the same reading this
    /// platform already gives a null attribute: absent, and absent never matches. Failing instead
    /// would mean a client that adds one unrelated object to its context stops getting any flags at
    /// all — a far worse outcome than one attribute no rule could have used. A datetime arrives as a
    /// JSON string and so survives as text, which is the only form three runtimes agree on anyway.
    /// </para>
    /// </summary>
    public static Result<FlagContext> BindContext(IReadOnlyDictionary<string, JsonElement>? context)
    {
        if (context is null)
            return Result.Success(FlagContext.Empty);

        string? key = null;
        var attributes = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);

        // targetingKey wins over the alias when both are present: it is the spec's field, and a
        // caller sending both has told us the same thing twice rather than two different things.
        if (TryReadKey(context, TargetingKey, out var targeting))
            key = targeting;
        else if (TryReadKey(context, TargetingKeyAlias, out var aliased))
            key = aliased;

        if (key is { Length: > MaxContextKeyLength })
            return Result.Failure<FlagContext>(OfrepErrors.ContextTooLarge);

        foreach (var field in context)
        {
            if (string.Equals(field.Key, TargetingKey, StringComparison.Ordinal)
                || string.Equals(field.Key, TargetingKeyAlias, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryReadAttribute(field.Value, out var value))
                continue;

            // Counted only once it is known to be usable, so a context carrying a few structures
            // alongside real traits is not penalised for fields this route already treats as absent.
            if (attributes.Count >= MaxAttributes)
                return Result.Failure<FlagContext>(OfrepErrors.ContextTooLarge);

            if (field.Key.Length > MaxAttributeNameLength)
                return Result.Failure<FlagContext>(OfrepErrors.ContextTooLarge);

            attributes[field.Key] = value;
        }

        return Result.Success(new FlagContext(key, attributes));
    }

    /// <summary>
    /// A tag for one set of answers: the ruleset's own tag folded together with the context they
    /// were computed for.
    ///
    /// <para>
    /// It cannot be the ruleset's tag alone. OFREP's bulk route is a POST whose answers depend on
    /// the body, so a client that changed its context and reused the tag it was last given would be
    /// told nothing had changed when everything had. Folding the context in makes a 304 mean what
    /// it says.
    /// </para>
    /// </summary>
    public static string ETagFor(string rulesetETag, FlagContext context)
    {
        var fingerprint = new StringBuilder();

        Append(fingerprint, "ffofrep/1");
        Append(fingerprint, rulesetETag);
        Append(fingerprint, context.Key ?? string.Empty);

        // Ordered, because a dictionary has none of its own and a tag that moves with enumeration
        // order is a tag that reports a change nobody made.
        foreach (var attribute in context.Attributes.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            Append(fingerprint, attribute.Key);
            Append(fingerprint, attribute.Value.ToCanonicalString());
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()));

        return $"\"{Base64Url.EncodeToString(hash)}\"";
    }

    private static bool TryReadKey(IReadOnlyDictionary<string, JsonElement> context, string name, out string? key)
    {
        key = null;

        if (!context.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;

        key = element.GetString();

        return key is not null;
    }

    private static bool TryReadAttribute(JsonElement element, out AttributeValue value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString() ?? string.Empty;
                value = AttributeValue.OfText(text);

                // An over-long string or a number past 2^53 is not something three engines can agree
                // on, so it is dropped for the same reason a structure is: it could never match.
                return value.IsRepresentable;

            case JsonValueKind.Number:
                if (!element.TryGetDouble(out var number))
                {
                    value = AttributeValue.OfText(string.Empty);
                    return false;
                }

                value = AttributeValue.OfNumber(number);

                return value.IsRepresentable;

            case JsonValueKind.True:
            case JsonValueKind.False:
                value = AttributeValue.OfBoolean(element.ValueKind == JsonValueKind.True);
                return true;

            // Null, objects and arrays: absent, per the note on BindContext.
            default:
                value = AttributeValue.OfText(string.Empty);
                return false;
        }
    }

    private static void Append(StringBuilder fingerprint, string part) =>
        fingerprint.Append(part.Length).Append(':').Append(part).Append('\n');
}

/// <summary>One flag's answer, in OFREP's <c>evaluationSuccess</c> shape.</summary>
/// <param name="Key">The flag's key.</param>
/// <param name="Value">The value served, as a bare JSON primitive.</param>
/// <param name="Variant">The variant it came from.</param>
/// <param name="Reason">Why — <c>STATIC</c>, <c>TARGETING_MATCH</c>, <c>DISABLED</c>, <c>DEFAULT</c>.</param>
/// <param name="Metadata">Provider metadata. An empty object, never absent, per the specification.</param>
public sealed record OfrepFlagResult(
    string Key,
    FlagValue Value,
    string? Variant,
    string Reason,
    IReadOnlyDictionary<string, AttributeValue> Metadata);

/// <summary>
/// One flag's failure, in OFREP's <c>evaluationFailure</c> shape.
///
/// <para>
/// Deliberately not ProblemDetails, unlike every other error this server produces. An OFREP client
/// parses this shape and reads <c>errorCode</c> to decide whether to fall back to the caller's own
/// default — handing it ProblemDetails would make the one field it needs unreadable. The
/// authentication failures above it stay ProblemDetails, because the protocol says nothing about
/// their bodies and a person debugging a rejected key is better served by the detailed form.
/// </para>
/// </summary>
public sealed record OfrepFlagFailure(
    string Key,
    [property: JsonPropertyName("errorCode")] string ErrorCode,
    [property: JsonPropertyName("errorDetails")] string ErrorDetails);

/// <summary>A whole request's failure, in OFREP's <c>bulkEvaluationFailure</c> shape.</summary>
public sealed record OfrepFailure(
    [property: JsonPropertyName("errorCode")] string ErrorCode,
    [property: JsonPropertyName("errorDetails")] string ErrorDetails);

/// <summary>What a caller can get wrong about an OFREP request.</summary>
public static class OfrepErrors
{
    public static Error ContextTooLarge => Error.Validation(
        EvaluationErrorCode.InvalidContext,
        $"The context is larger than this endpoint accepts. It takes at most {Ofrep.MaxAttributes} " +
        $"attributes, attribute names of {Ofrep.MaxAttributeNameLength} characters or fewer, and a " +
        $"targeting key of {Ofrep.MaxContextKeyLength} characters or fewer.");

    public static Error MalformedBody => Error.Validation(
        EvaluationErrorCode.ParseError,
        "The request body could not be read as JSON.");

    public static Error FlagNotFound(string key) => Error.NotFound(
        EvaluationErrorCode.FlagNotFound,
        $"No flag with the key '{key}' exists in this environment.");
}
