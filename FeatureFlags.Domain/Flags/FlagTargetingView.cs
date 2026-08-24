using FeatureFlags.Domain.Environments;

namespace FeatureFlags.Domain.Flags;

/// <summary>
/// One place a segment is being used: a flag, and the environment whose state names it.
///
/// <para>
/// A fact about flags rather than about segments, which is why it is read through
/// <see cref="IFlagViewRepository"/> — a segment does not know who points at it, and giving it a
/// query that implied otherwise would put <c>Domain/Segments</c> in the business of knowing
/// <c>Domain/Flags</c> exists.
/// </para>
/// </summary>
public sealed record FlagTargetingView(FlagKey Key, string Name, EnvironmentKey Environment);
