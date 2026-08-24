using System;
using System.Collections.Generic;

namespace FeatureFlags.Evaluation;

/// <summary>
/// Whether a context is in a segment.
///
/// <para>
/// Pure, static, and allocation-light on purpose: this runs on the hot path of every application
/// that reads a flag, and it runs identically in the server, in the .NET client, and — reimplemented
/// against the same conformance vectors — in the Node client.
/// </para>
/// </summary>
public static class SegmentMatcher
{
    /// <summary>
    /// The order below is the definition, not an optimisation.
    ///
    /// <para>
    /// Exclusion is absolute, because the reason to exclude somebody is usually that something is
    /// broken for them; an include list or a condition that could overrule it would make the escape
    /// hatch unreliable exactly when it is needed. Inclusion then short-circuits the conditions,
    /// because naming a key is how "one account I am debugging" is expressed and it should not also
    /// require them to satisfy a rule written for everybody else.
    /// </para>
    /// <para>
    /// An empty segment — no included keys and no conditions — matches <em>nobody</em>. The other
    /// reading, that a definition with no restrictions restricts nothing and so admits everyone, is
    /// defensible right up until a half-finished segment is saved and silently turns a flag on for
    /// the world.
    /// </para>
    /// </summary>
    public static bool Matches(RulesetSegment? segment, FlagContext? context)
    {
        if (segment is null)
            return false;

        context ??= FlagContext.Empty;

        if (context.Key is not null && ContainsOrdinal(segment.Excluded, context.Key))
            return false;

        if (context.Key is not null && ContainsOrdinal(segment.Included, context.Key))
            return true;

        // No conditions and the key was not named: there is nothing here that could admit anybody.
        if (segment.Conditions.Count == 0)
            return false;

        for (var i = 0; i < segment.Conditions.Count; i++)
        {
            if (!Satisfies(segment.Conditions[i], context))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether one condition holds. An absent attribute never satisfies anything — there is no
    /// default value for a trait the application did not send, and inventing one would make a
    /// segment match people nobody described.
    /// </summary>
    public static bool Satisfies(RulesetCondition? condition, FlagContext? context)
    {
        if (condition?.Attribute is null || condition.Operator is null)
            return false;

        context ??= FlagContext.Empty;

        if (!context.TryGetAttribute(condition.Attribute, out var actual) || actual is null)
            return false;

        return condition.Operator switch
        {
            ConditionOperatorNames.EqualTo or ConditionOperatorNames.OneOf =>
                AnyValue(condition, actual.Equals),

            ConditionOperatorNames.Contains =>
                AnyText(condition, actual, (subject, candidate) =>
                    subject.IndexOf(candidate, StringComparison.Ordinal) >= 0),

            ConditionOperatorNames.StartsWith =>
                AnyText(condition, actual, (subject, candidate) =>
                    subject.StartsWith(candidate, StringComparison.Ordinal)),

            ConditionOperatorNames.EndsWith =>
                AnyText(condition, actual, (subject, candidate) =>
                    subject.EndsWith(candidate, StringComparison.Ordinal)),

            ConditionOperatorNames.GreaterThan =>
                AnyNumber(condition, actual, (subject, candidate) => subject > candidate),

            ConditionOperatorNames.GreaterThanOrEqual =>
                AnyNumber(condition, actual, (subject, candidate) => subject >= candidate),

            ConditionOperatorNames.LessThan =>
                AnyNumber(condition, actual, (subject, candidate) => subject < candidate),

            ConditionOperatorNames.LessThanOrEqual =>
                AnyNumber(condition, actual, (subject, candidate) => subject <= candidate),

            // An operator this build has never heard of. A client one release behind the console
            // must not throw over a segment it cannot evaluate, and must not guess either — so it
            // does not match, which is the same answer it would give if the condition failed.
            _ => false,
        };
    }

    private static bool AnyValue(RulesetCondition condition, Func<AttributeValue, bool> predicate)
    {
        for (var i = 0; i < condition.Values.Count; i++)
        {
            var candidate = condition.Values[i];

            if (candidate is not null && predicate(candidate))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A string operator over a non-string attribute is a non-match, never a rendering. The whole
    /// point of typing these values is that <c>accountAge contains "4"</c> is not a question this
    /// system answers.
    /// </summary>
    private static bool AnyText(RulesetCondition condition, AttributeValue actual, Func<string, string, bool> predicate)
    {
        if (actual.Kind != AttributeValueKind.Text)
            return false;

        return AnyValue(condition, candidate =>
            candidate.Kind == AttributeValueKind.Text && predicate(actual.Text, candidate.Text));
    }

    private static bool AnyNumber(RulesetCondition condition, AttributeValue actual, Func<double, double, bool> predicate)
    {
        if (actual.Kind != AttributeValueKind.Number)
            return false;

        return AnyValue(condition, candidate =>
            candidate.Kind == AttributeValueKind.Number && predicate(actual.Number, candidate.Number));
    }

    private static bool ContainsOrdinal(IReadOnlyList<string> keys, string key)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
