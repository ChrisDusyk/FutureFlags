using System;
using System.Globalization;

namespace FutureFlags.Evaluation;

/// <summary>Which of the four things a flag's value is.</summary>
public enum FlagValueKind
{
    /// <summary>True or false. The only kind this build can author.</summary>
    Boolean,

    /// <summary>A string.</summary>
    String,

    /// <summary>An IEEE-754 binary64 number.</summary>
    Number,

    /// <summary>A JSON object or array, carried as its raw text.</summary>
    Object,
}

/// <summary>
/// One flag value: a bool, a string, an IEEE-754 double, or a JSON structure.
///
/// <para>
/// The four kinds are OpenFeature's four flag types, and they are here in full even though this
/// build can only author <see cref="FlagValueKind.Boolean"/>. That is the point: the wire shape,
/// the persisted event payload, and the ruleset all carry a typed value from today, so adding
/// string, number, or object flags later is a domain change rather than a second breaking change
/// to an event stream that cannot be rolled back.
/// </para>
/// <para>
/// Not to be confused with <see cref="AttributeValue"/>, which is the *context* side — what a
/// caller knows about the subject being evaluated. The two are deliberately separate types with
/// separate limits: an attribute is something to match on, a flag value is something to serve.
/// Conflating them would mean a flag could only ever hold what a segment condition can compare.
/// </para>
/// <para>
/// The converter is declared on the type for the same reason it is on <see cref="AttributeValue"/>:
/// the places this has to serialize correctly build their own options, and one that forgot would
/// fail only where it was hardest to notice.
/// </para>
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(FlagValueJsonConverter))]
public sealed record FlagValue
{
    /// <summary>The longest string a <see cref="FlagValueKind.String"/> value may carry.</summary>
    public const int MaxTextLength = 4096;

    /// <summary>The longest raw JSON a <see cref="FlagValueKind.Object"/> value may carry.</summary>
    public const int MaxObjectJsonLength = 16384;

    /// <summary>
    /// The largest magnitude a number may carry, for the same reason as
    /// <see cref="AttributeValue.MaxMagnitude"/> — past 2^53 a double and a JavaScript number stop
    /// agreeing on which integers exist.
    /// </summary>
    public const double MaxMagnitude = 9007199254740992d;

    private FlagValue(FlagValueKind kind, bool boolean, string text, double number)
    {
        Kind = kind;
        Boolean = boolean;
        Text = text;
        Number = number;
    }

    /// <summary>A true flag value. Shared, because this is by far the most common one.</summary>
    public static FlagValue True { get; } = new(FlagValueKind.Boolean, true, string.Empty, 0d);

    /// <summary>A false flag value.</summary>
    public static FlagValue False { get; } = new(FlagValueKind.Boolean, false, string.Empty, 0d);

    /// <summary>Which of the four things this is.</summary>
    public FlagValueKind Kind { get; }

    /// <summary>Meaningful only when <see cref="Kind"/> is <see cref="FlagValueKind.Boolean"/>, and
    /// false otherwise.</summary>
    public bool Boolean { get; }

    /// <summary>
    /// The string for <see cref="FlagValueKind.String"/>, or the raw JSON text for
    /// <see cref="FlagValueKind.Object"/>. <see cref="string.Empty"/> for the other kinds rather
    /// than null, so equality never has a null to reason about.
    /// </summary>
    public string Text { get; }

    /// <summary>Meaningful only when <see cref="Kind"/> is <see cref="FlagValueKind.Number"/>, and
    /// zero otherwise.</summary>
    public double Number { get; }

    /// <summary>A true/false value.</summary>
    public static FlagValue OfBoolean(bool value) => value ? True : False;

    /// <summary>A string value. A null is folded to the empty string rather than refused.</summary>
    public static FlagValue OfString(string? value) =>
        new(FlagValueKind.String, false, value ?? string.Empty, 0d);

    /// <summary>A numeric value. See <see cref="IsRepresentable"/> for what will not survive the
    /// trip to another runtime.</summary>
    public static FlagValue OfNumber(double value) =>
        new(FlagValueKind.Number, false, string.Empty, value);

    /// <summary>A JSON object or array, given as its raw text. The text is carried verbatim rather
    /// than reparsed, so that what a caller stored is exactly what a caller is served.</summary>
    public static FlagValue OfObject(string? json) =>
        new(FlagValueKind.Object, false, json ?? "{}", 0d);

    /// <summary>
    /// Whether this is a value the three engines can agree on.
    ///
    /// <para>
    /// For <see cref="FlagValueKind.Object"/> that means the raw text has to actually parse, and to
    /// parse as an object or an array. <see cref="FlagValueJsonConverter"/> emits it with
    /// <c>WriteRawValue</c>, which validates and throws — so without this check an unparseable value
    /// would be accepted by <c>FlagVariants.Create</c> and then fail much later, while serializing a
    /// ruleset or writing an event, a long way from the caller that supplied it. Text that parses
    /// but is a bare number or string is refused for the other half of the same reason: the kind
    /// would say object while the token on the wire said otherwise.
    /// </para>
    /// <para>
    /// This is a validation-path property, not an evaluation one — the only caller is
    /// <c>FlagVariants.Create</c> — so the parse costs nothing that matters.
    /// </para>
    /// </summary>
    public bool IsRepresentable => Kind switch
    {
        FlagValueKind.String => Text.Length <= MaxTextLength,
        FlagValueKind.Object => Text.Length <= MaxObjectJsonLength && IsJsonStructure(Text),
        FlagValueKind.Number => !double.IsNaN(Number)
            && !double.IsInfinity(Number)
            && Math.Abs(Number) <= MaxMagnitude,
        _ => true,
    };

    private static bool IsJsonStructure(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);

            return document.RootElement.ValueKind
                is System.Text.Json.JsonValueKind.Object
                or System.Text.Json.JsonValueKind.Array;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// A stable rendering, used where a value has to be fingerprinted rather than compared. The
    /// kind leads it, so that <c>"1"</c>, <c>1</c>, and <c>true</c> can never collide.
    /// </summary>
    public string ToCanonicalString() => Kind switch
    {
        FlagValueKind.String => "s:" + Text,
        FlagValueKind.Object => "o:" + Text,
        FlagValueKind.Number => "n:" + Number.ToString("R", CultureInfo.InvariantCulture),
        _ => Boolean ? "b:1" : "b:0",
    };

    /// <inheritdoc cref="ToCanonicalString"/>
    public override string ToString() => ToCanonicalString();
}
