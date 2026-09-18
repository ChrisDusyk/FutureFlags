using System;

namespace FutureFlags.Evaluation;

/// <summary>
/// The variant names a boolean flag carries.
///
/// <para>
/// Every flag in this build is boolean and has exactly these two variants, so these names appear in
/// every resolution. They are constants rather than literals because three implementations and a
/// set of conformance vectors all have to spell them the same way, and because a flag whose value
/// type is not boolean will one day name its own variants — at which point the fallbacks these
/// represent need to be findable rather than scattered.
/// </para>
/// </summary>
public static class FlagVariantNames
{
    /// <summary>The variant served when the flag is on and reaches this context.</summary>
    public const string On = "on";

    /// <summary>The variant served otherwise — off in this environment, or targeted at segments
    /// this context is not in.</summary>
    public const string Off = "off";
}

/// <summary>The value types a flag can hold, as they appear on the wire.</summary>
public static class FlagValueTypeNames
{
    /// <summary>True or false. The only type this build can author.</summary>
    public const string Boolean = "boolean";

    /// <summary>A string.</summary>
    public const string String = "string";

    /// <summary>An IEEE-754 binary64 number.</summary>
    public const string Number = "number";

    /// <summary>A JSON object or array.</summary>
    public const string Object = "object";
}
