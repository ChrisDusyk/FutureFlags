using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;

namespace FeatureFlags.Infrastructure.Persistence.ReadModels;

/// <summary>
/// The current state of a flag as projected for reading — what <c>feature_flags</c>/
/// <c>feature_flag_states</c> hold. A plain mutable row, not a domain entity: it protects no
/// invariant, since every value it carries was already validated on the way into an event.
/// </summary>
internal sealed class FlagRow
{
    public Guid Id { get; set; }
    public FlagKey Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<FlagStateRow> States { get; set; } = [];
}

internal sealed class FlagStateRow
{
    public EnvironmentKey Environment { get; set; } = null!;
    public bool IsEnabled { get; set; }

    /// <summary>
    /// The segment keys this environment targets, as a Postgres <c>text[]</c>. Plain strings rather
    /// than <see cref="Domain.Segments.SegmentKey"/>: there is no foreign key to <c>segments</c>
    /// either, because a retired segment must not stop a flag being read — every engine already
    /// treats a key it cannot resolve as a non-match.
    /// </summary>
    public List<string> TargetedSegments { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }
}
