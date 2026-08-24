using System;
using System.Collections.Generic;
using System.Globalization;

namespace FeatureFlags.Evaluation;

/// <summary>Which of the three things a context attribute is.</summary>
public enum AttributeValueKind
{
    /// <summary>A string.</summary>
    Text,

    /// <summary>An IEEE-754 binary64 number.</summary>
    Number,

    /// <summary>True or false.</summary>
    Boolean,
}

/// <summary>
/// One typed value: a string, an IEEE-754 double, or a bool.
///
/// <para>
/// There is no fourth case and no coercion between the three — a number compared to a string is a
/// non-match, never a parse. That rule is the whole reason this type exists rather than a bare
/// <see cref="object"/>: three engines in three runtimes have to agree on the answer, and the only
/// way they can is if none of them is allowed to be clever.
/// </para>
/// <para>
/// The converter is declared on the type rather than registered in a list of options, because the
/// places this has to serialize correctly are not all under one roof: model binding, a ruleset
/// payload, an event's jsonb column, and whatever cache tier a HybridCache entry lands in. Each of
/// those builds its own options, and one that forgot would fail only where it was hardest to
/// notice — an in-memory cache would round-trip a definition that Redis silently could not.
/// </para>
/// <para>
/// <see cref="double"/> and not <see cref="decimal"/>, deliberately. JavaScript has one number type
/// and it is IEEE-754 binary64; a decimal on the server is a value the browser engine cannot
/// reproduce, and a segment that matches in one place and not the other is worse than one that
/// cannot express a hundredth of a cent.
/// </para>
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(AttributeValueJsonConverter))]
public sealed record AttributeValue
{
    /// <summary>The longest string a context attribute or a condition value may carry.</summary>
    public const int MaxTextLength = 512;

    /// <summary>
    /// The largest magnitude a number may carry. Past 2^53 a double and a JavaScript number stop
    /// agreeing on which integers exist, and this value has to mean the same thing in three places.
    /// </summary>
    public const double MaxMagnitude = 9007199254740992d;

    private AttributeValue(AttributeValueKind kind, string text, double number, bool boolean)
    {
        Kind = kind;
        Text = text;
        Number = number;
        Boolean = boolean;
    }

    /// <summary>Which of the three things this is.</summary>
    public AttributeValueKind Kind { get; }

    /// <summary>
    /// Meaningful only when <see cref="Kind"/> is <see cref="AttributeValueKind.Text"/>, and
    /// <see cref="string.Empty"/> otherwise rather than null — so that equality never has a null to
    /// reason about and no reader needs a guard before comparing.
    /// </summary>
    public string Text { get; }

    /// <summary>Meaningful only when <see cref="Kind"/> is <see cref="AttributeValueKind.Number"/>,
    /// and zero otherwise.</summary>
    public double Number { get; }

    /// <summary>Meaningful only when <see cref="Kind"/> is <see cref="AttributeValueKind.Boolean"/>,
    /// and false otherwise.</summary>
    public bool Boolean { get; }

    /// <summary>A string value. A null is folded to the empty string rather than refused, so that
    /// no caller has to decide what an absent string means.</summary>
    public static AttributeValue OfText(string? value) =>
        new(AttributeValueKind.Text, value ?? string.Empty, 0d, false);

    /// <summary>A numeric value. See <see cref="IsRepresentable"/> for what will not survive the
    /// trip to another runtime.</summary>
    public static AttributeValue OfNumber(double value) =>
        new(AttributeValueKind.Number, string.Empty, value, false);

    /// <summary>A true/false value.</summary>
    public static AttributeValue OfBoolean(bool value) =>
        new(AttributeValueKind.Boolean, string.Empty, 0d, value);

    /// <summary>
    /// Whether this is a value the three engines can agree on.
    ///
    /// <para>
    /// NaN and the infinities have no JSON representation at all. NaN is refused for a second
    /// reason worth knowing: <c>double.NaN != double.NaN</c>, so a definition containing one is
    /// never equal to itself, and every "did anything actually change" comparison in the domain
    /// would quietly start raising an event on every save.
    /// </para>
    /// </summary>
    public bool IsRepresentable => Kind switch
    {
        AttributeValueKind.Text => Text.Length <= MaxTextLength,
        AttributeValueKind.Number => !double.IsNaN(Number)
            && !double.IsInfinity(Number)
            && Math.Abs(Number) <= MaxMagnitude,
        _ => true,
    };

    /// <summary>
    /// A stable rendering, used where a value has to be ordered or fingerprinted rather than
    /// compared. The kind leads it, so that <c>"1"</c>, <c>1</c>, and <c>true</c> can never collide.
    /// </summary>
    public string ToCanonicalString() => Kind switch
    {
        AttributeValueKind.Text => "s:" + Text,
        AttributeValueKind.Number => "n:" + Number.ToString("R", CultureInfo.InvariantCulture),
        _ => Boolean ? "b:1" : "b:0",
    };

    /// <inheritdoc cref="ToCanonicalString"/>
    public override string ToString() => ToCanonicalString();

    /// <summary>Ordinal ordering over <see cref="ToCanonicalString"/>, so a set of values sorts the
    /// same way in every runtime.</summary>
    public static IComparer<AttributeValue> CanonicalComparer { get; } = new CanonicalOrder();

    private sealed class CanonicalOrder : IComparer<AttributeValue>
    {
        public int Compare(AttributeValue? x, AttributeValue? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            return string.CompareOrdinal(x.ToCanonicalString(), y.ToCanonicalString());
        }
    }
}
