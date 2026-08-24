using System;
using System.Collections.Generic;

namespace FeatureFlags.Evaluation;

/// <summary>
/// Who is being asked about: an optional subject key, and whatever traits the calling application
/// chose to describe them with.
///
/// <para>
/// Attribute names are normalised to lowercase on the way in, the same treatment a
/// <c>FlagKey</c> gets, so that a segment written against <c>plan</c> matches a context that sent
/// <c>Plan</c>. Attribute <em>values</em> and <see cref="Key"/> are left exactly as given and are
/// compared ordinally: case-insensitive comparison across .NET and JavaScript means picking a
/// culture, and <c>InvariantCultureIgnoreCase</c> and <c>toLowerCase()</c> do not agree on every
/// alphabet. Ordinal is the one rule three runtimes agree on for free.
/// </para>
/// <para>
/// An empty context is a real and useful thing rather than a missing argument — it is what an
/// application that has not described anybody sends, and it matches no segment, which is the safe
/// direction for a flag that has been targeted.
/// </para>
/// </summary>
public sealed class FlagContext(string? key, IReadOnlyDictionary<string, AttributeValue>? attributes)
{
    private static readonly IReadOnlyDictionary<string, AttributeValue> NoAttributes =
        new Dictionary<string, AttributeValue>(StringComparer.Ordinal);

    /// <summary>The empty context: nobody in particular, described by nothing.</summary>
    public static FlagContext Empty { get; } = new(null, null);

    /// <summary>
    /// What the application calls this subject — a user id, an account id, a device. Null when the
    /// caller did not say, in which case a segment's include and exclude lists can never match.
    /// </summary>
    public string? Key { get; } = key;

    /// <summary>The traits, by lowercase name.</summary>
    public IReadOnlyDictionary<string, AttributeValue> Attributes { get; } = Normalise(attributes);

    /// <summary>Looks an attribute up by name, folding the name the same way the constructor did.
    /// False when the context did not carry one, which is always a non-match rather than a
    /// default.</summary>
    /// <summary>A context describing this subject and nothing else yet. The start of a chain of
    /// <see cref="With(string, string)"/> calls.</summary>
    public static FlagContext For(string? key) => new(key, null);

    /// <summary>This context plus one text attribute. Returns a new instance — a context is
    /// immutable, so one built once and held for a process cannot be changed under a reader.</summary>
    public FlagContext With(string name, string value) => With(name, AttributeValue.OfText(value));

    /// <summary>This context plus one numeric attribute.</summary>
    public FlagContext With(string name, double value) => With(name, AttributeValue.OfNumber(value));

    /// <summary>This context plus one true/false attribute.</summary>
    public FlagContext With(string name, bool value) => With(name, AttributeValue.OfBoolean(value));

    /// <summary>This context plus one already-typed attribute.</summary>
    public FlagContext With(string name, AttributeValue value)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        var combined = new Dictionary<string, AttributeValue>(Attributes.Count + 1, StringComparer.Ordinal);

        foreach (var pair in Attributes)
            combined[pair.Key] = pair.Value;

        combined[NormaliseName(name)] = value;

        return new FlagContext(Key, combined);
    }

    /// <summary>
    /// This context laid over another. Everything here wins, and anything only
    /// <paramref name="defaults"/> carries is kept — which is what lets an application set the
    /// traits that never change once, at registration, and still describe a user per call.
    /// </summary>
    public FlagContext WithDefaults(FlagContext? defaults)
    {
        if (defaults is null || (defaults.Key is null && defaults.Attributes.Count == 0))
            return this;

        var combined = new Dictionary<string, AttributeValue>(
            defaults.Attributes.Count + Attributes.Count, StringComparer.Ordinal);

        foreach (var pair in defaults.Attributes)
            combined[pair.Key] = pair.Value;

        foreach (var pair in Attributes)
            combined[pair.Key] = pair.Value;

        return new FlagContext(Key ?? defaults.Key, combined);
    }

    /// <summary>Looks an attribute up by name, folding the name the same way this context folded
    /// its own. False when the context did not carry one, which is always a non-match rather than
    /// a default.</summary>
    public bool TryGetAttribute(string name, out AttributeValue value)
    {
        if (name is null)
        {
            value = null!;
            return false;
        }

        return Attributes.TryGetValue(NormaliseName(name), out value!);
    }

    /// <summary>The one place an attribute name is folded, so the domain, the console, and both
    /// SDKs cannot disagree about what counts as the same name.</summary>
    public static string NormaliseName(string name) => name.Trim().ToLowerInvariant();

    private static IReadOnlyDictionary<string, AttributeValue> Normalise(
        IReadOnlyDictionary<string, AttributeValue>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
            return NoAttributes;

        var normalised = new Dictionary<string, AttributeValue>(attributes.Count, StringComparer.Ordinal);

        foreach (var pair in attributes)
        {
            if (pair.Key is null || pair.Value is null)
                continue;

            // Last one wins when two spellings fold together. There is no better answer, and
            // silently dropping one would be worse than picking one.
            normalised[NormaliseName(pair.Key)] = pair.Value;
        }

        return normalised;
    }
}
