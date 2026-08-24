using System;
using System.Collections.Generic;

namespace FeatureFlags.Evaluation;

/// <summary>
/// The operator names as they appear on the wire and in a stored definition.
///
/// <para>
/// Plain strings rather than the domain's <c>ConditionOperator</c> value object, because this file
/// is compiled into the client package as well and may not depend on <c>Result</c>. The domain owns
/// the validating type; this owns the vocabulary, and one test
/// (<c>ConditionOperatorNamesAreInStepTests</c>) asserts the two sets are the same. That test is
/// the seam — if it ever fails, an operator exists that one side can write and the other cannot read.
/// </para>
/// <para>
/// There is no <c>matches</c>. A regular-expression operator is safe on the server
/// (<c>RegexOptions.NonBacktracking</c>) and cannot be made safe in the browser, where there is no
/// match timeout and no linear-time engine — and validating patterns server-side does not help,
/// because the canonical catastrophic pattern <c>(a+)+b</c> compiles happily under
/// <c>NonBacktracking</c>. An operator that is linear in two engines and a hang in the third is
/// worse than a missing one.
/// </para>
/// </summary>
public static class ConditionOperatorNames
{
    /// <summary>The attribute is exactly this value, of exactly this type.</summary>
    public const string EqualTo = "equals";
    /// <summary>The attribute is any one of these values. Same predicate as <see cref="EqualTo"/>, with more than one candidate.</summary>
    public const string OneOf = "one-of";
    /// <summary>The attribute is a string containing this substring. Ordinal.</summary>
    public const string Contains = "contains";
    /// <summary>The attribute is a string beginning with this one. Ordinal.</summary>
    public const string StartsWith = "starts-with";
    /// <summary>The attribute is a string ending with this one. Ordinal.</summary>
    public const string EndsWith = "ends-with";
    /// <summary>The attribute is a number strictly above this one.</summary>
    public const string GreaterThan = "greater-than";
    /// <summary>The attribute is a number at or above this one.</summary>
    public const string GreaterThanOrEqual = "greater-than-or-equal";
    /// <summary>The attribute is a number strictly below this one.</summary>
    public const string LessThan = "less-than";
    /// <summary>The attribute is a number at or below this one.</summary>
    public const string LessThanOrEqual = "less-than-or-equal";

    /// <summary>Every operator this build understands, in the order the console offers them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        EqualTo,
        OneOf,
        Contains,
        StartsWith,
        EndsWith,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
    ];

    /// <summary>Whether this build knows how to evaluate an operator by this name.</summary>
    public static bool IsRecognised(string? name)
    {
        if (name is null)
            return false;

        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i], name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
