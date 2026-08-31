namespace FutureFlags.Server.Features.Flags.EvaluateFlags;

/// <summary>
/// What a client needs to answer "is this on?" and nothing else.
///
/// <para>
/// Deliberately not <c>FlagSummary</c>: no id, no name, no description, no timestamps. Those are
/// the console's business, and this is the response a fleet of application servers fetches on a
/// loop. A key and a boolean is the whole of it.
/// </para>
/// </summary>
public sealed record EvaluateFlagsResponse(string Environment, IReadOnlyDictionary<string, bool> Flags);
