namespace FeatureFlags.Domain.Segments;

/// <summary>
/// A segment as projected for reading. Unlike <see cref="Segment"/> this is not sourced from events
/// at read time and carries no history — it is what the write side's events most recently produced.
///
/// <para>
/// A retired segment never appears as one of these: <see cref="ISegmentViewRepository"/> filters
/// tombstones out, so nothing downstream has to remember to.
/// </para>
/// </summary>
public sealed record SegmentView(
    Guid Id,
    SegmentKey Key,
    string Name,
    string Description,
    SegmentDefinition Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
