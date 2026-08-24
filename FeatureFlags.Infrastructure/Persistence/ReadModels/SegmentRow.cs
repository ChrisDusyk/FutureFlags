using FeatureFlags.Domain.Segments;

namespace FeatureFlags.Infrastructure.Persistence.ReadModels;

/// <summary>
/// The current state of a segment as projected for reading — what <c>segments</c> holds. A plain
/// mutable row, not a domain entity: it protects no invariant, since every value it carries was
/// already validated on the way into an event.
/// </summary>
internal sealed class SegmentRow
{
    public Guid Id { get; set; }

    public SegmentKey Key { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    /// <summary>
    /// The whole definition as one <c>jsonb</c> column rather than relational child tables. It is a
    /// projection rebuilt from events, it is read and written whole, and the only query that ever
    /// looks inside it is a person reading it on screen.
    /// </summary>
    public SegmentDefinition Definition { get; set; } = SegmentDefinition.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// The tombstone. Set rather than deleting the row, so that <c>segment_events</c> stays
    /// reachable — the repository finds a stream by going row to id to replay, and there is no
    /// other path. It is also what stops the key being reissued to something unrelated.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
