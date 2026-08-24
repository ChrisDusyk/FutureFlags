using FeatureFlags.Domain.Segments.Events;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Domain.Segments;

/// <summary>
/// A named group of people a feature can reach — beta testers, internal staff, one account being
/// debugged. Identified by its <see cref="SegmentKey"/>.
///
/// <para>
/// A segment's definition is <em>global</em>, not per-environment. A flag's identity is global and
/// only its state varies by environment; a segment follows the same shape, and it is what "change
/// the definition and every rule using it follows" has to mean. What varies per environment is
/// which flags point at it, which is a fact about flags.
/// </para>
/// <para>
/// Event-sourced on the same terms as <see cref="Flags.FeatureFlag"/>: state lives nowhere but the
/// events in <see cref="UncommittedEvents"/> and whatever a prior <see cref="Rehydrate"/> folded in,
/// and every mutator changes a fact by raising an event rather than by setting a field. Timestamps
/// are supplied by the caller rather than read from a clock, so the entity stays deterministic.
/// </para>
/// </summary>
public sealed class Segment
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;

    private readonly List<ISegmentEvent> _uncommittedEvents = [];

    // IDE0032 suggests auto properties here, which would leave settable members for EF to pick up
    // by convention if this type is ever queried directly — the same reasoning as FeatureFlag's.
#pragma warning disable IDE0032
    private int _version;
    private DateTimeOffset? _deletedAt;
#pragma warning restore IDE0032

    private Segment(Guid id)
    {
        Id = id;
        Key = null!;
        Name = null!;
        Description = null!;
        Definition = SegmentDefinition.Empty;
    }

    public Guid Id { get; private set; }

    public SegmentKey Key { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    /// <summary>Who is in it. See <see cref="SegmentDefinition"/>.</summary>
    public SegmentDefinition Definition { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When anything about this segment last changed — its details or its definition.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When it was retired, if it was.</summary>
    public Option<DateTimeOffset> DeletedAt => _deletedAt.ToOption();

    public bool IsDeleted => _deletedAt is not null;

    /// <summary>How many events have been applied to this instance, fresh or replayed — its place
    /// in its own stream.</summary>
    public int Version => _version;

    /// <summary>Events raised since this instance was created or rehydrated, not yet appended.
    /// Wrapped rather than handing out the list itself, so a caller cannot clear it out from under
    /// the aggregate.</summary>
    public IReadOnlyList<ISegmentEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    public static Result<Segment> Create(
        string? key,
        string? name,
        string? description,
        SegmentDefinition? definition,
        DateTimeOffset timestamp,
        Guid causedBy)
    {
        var keyResult = SegmentKey.Create(key);
        if (keyResult.IsFailure)
            return Result.Failure<Segment>(keyResult.Error);

        var detailsResult = ValidateDetails(name, description);
        if (detailsResult.IsFailure)
            return Result.Failure<Segment>(detailsResult.Error);

        var (trimmedName, trimmedDescription) = detailsResult.Value;

        var segment = new Segment(Guid.CreateVersion7());

        segment.Raise(new SegmentCreatedEvent(
            segment.Id, keyResult.Value, trimmedName, trimmedDescription, timestamp, causedBy));

        // Raised separately rather than carried on the created event, so that folding a definition
        // happens in exactly one place — the same reason FeatureFlag.Create raises its state changes
        // rather than embedding them.
        segment.Raise(new SegmentDefinitionChangedEvent(
            segment.Id, definition ?? SegmentDefinition.Empty, timestamp, causedBy));

        return Result.Success(segment);
    }

    /// <summary>
    /// Rebuilds a segment by folding its full event history in order — no business validation, since
    /// every event already happened. The result carries no uncommitted events: replaying history is
    /// not the same as making it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The stream belongs to a different segment, or its first event is not a
    /// <see cref="SegmentCreatedEvent"/>. Both mean the repository handed this a corrupted or
    /// mismatched stream — a store bug rather than something a caller can recover from.
    /// </exception>
    public static Segment Rehydrate(Guid id, IEnumerable<ISegmentEvent> events)
    {
        var segment = new Segment(id);

        foreach (var @event in events)
        {
            if (@event.SegmentId != id)
                throw new InvalidOperationException(
                    $"Cannot rehydrate segment {id}: encountered an event for segment {@event.SegmentId}.");

            segment.Apply(@event);
        }

        if (segment.Key is null)
            throw new InvalidOperationException($"Cannot rehydrate segment {id}: its stream contains no SegmentCreatedEvent.");

        return segment;
    }

    /// <summary>
    /// Updates the name and/or description. Idempotent — submitting the values it already has raises
    /// no event and leaves <see cref="UpdatedAt"/> untouched. The key is never part of this: there
    /// is no parameter for it, and no other way to change it.
    /// </summary>
    public Result UpdateDetails(string? name, string? description, DateTimeOffset timestamp, Guid causedBy)
    {
        if (IsDeleted)
            return Result.Failure(SegmentErrors.Deleted(Key));

        var detailsResult = ValidateDetails(name, description);
        if (detailsResult.IsFailure)
            return detailsResult;

        var (trimmedName, trimmedDescription) = detailsResult.Value;

        if (trimmedName == Name && trimmedDescription == Description)
            return Result.Success();

        Raise(new SegmentDetailsChangedEvent(Id, trimmedName, trimmedDescription, timestamp, causedBy));

        return Result.Success();
    }

    /// <summary>
    /// Replaces who is in the segment. Idempotent on the same terms as everything else here, which
    /// is why <see cref="SegmentDefinition"/> goes to the trouble of having a normal form — without
    /// it, saving the editor unchanged would raise an event and churn every SDK's ETag.
    /// </summary>
    public Result ChangeDefinition(SegmentDefinition? definition, DateTimeOffset timestamp, Guid causedBy)
    {
        if (IsDeleted)
            return Result.Failure(SegmentErrors.Deleted(Key));

        var replacement = definition ?? SegmentDefinition.Empty;

        if (replacement == Definition)
            return Result.Success();

        Raise(new SegmentDefinitionChangedEvent(Id, replacement, timestamp, causedBy));

        return Result.Success();
    }

    /// <summary>
    /// Retires the segment.
    ///
    /// <para>
    /// A tombstone rather than a removal, and the key is never reissued. The repository finds a
    /// stream by going row → id → replay, so dropping the row would strand the events permanently:
    /// unrehydratable, no history, and a later segment created with the same key would silently
    /// point at an unrelated stream.
    /// </para>
    /// <para>
    /// Whether anything still points at this segment is not a question the aggregate can answer —
    /// targeting is a fact about flags — so the refusal lives in the handler, not here.
    /// </para>
    /// </summary>
    public Result Delete(DateTimeOffset timestamp, Guid causedBy)
    {
        if (IsDeleted)
            return Result.Failure(SegmentErrors.AlreadyDeleted(Key));

        Raise(new SegmentDeletedEvent(Id, timestamp, causedBy));

        return Result.Success();
    }

    /// <summary>Clears events once the repository has durably appended them.</summary>
    internal void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    private static Result<(string Name, string Description)> ValidateDetails(string? name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<(string, string)>(SegmentErrors.NameRequired);

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
            return Result.Failure<(string, string)>(SegmentErrors.NameTooLong);

        var trimmedDescription = description?.Trim() ?? string.Empty;
        if (trimmedDescription.Length > MaxDescriptionLength)
            return Result.Failure<(string, string)>(SegmentErrors.DescriptionTooLong);

        return Result.Success((trimmedName, trimmedDescription));
    }

    private void Raise(ISegmentEvent @event)
    {
        Apply(@event);
        _uncommittedEvents.Add(@event);
    }

    /// <summary>The one place a fact — fresh or replayed — is folded into this instance's state.</summary>
    private void Apply(ISegmentEvent @event)
    {
        switch (@event)
        {
            case SegmentCreatedEvent created:
                Id = created.SegmentId;
                Key = created.Key;
                Name = created.Name;
                Description = created.Description;
                CreatedAt = created.OccurredAt;
                UpdatedAt = created.OccurredAt;
                break;

            case SegmentDetailsChangedEvent detailsChanged:
                Name = detailsChanged.Name;
                Description = detailsChanged.Description;
                UpdatedAt = detailsChanged.OccurredAt;
                break;

            case SegmentDefinitionChangedEvent definitionChanged:
                Definition = definitionChanged.Definition;
                UpdatedAt = definitionChanged.OccurredAt;
                break;

            case SegmentDeletedEvent deleted:
                _deletedAt = deleted.OccurredAt;
                UpdatedAt = deleted.OccurredAt;
                break;

            // An event type this build does not know about is a corrupted or forward-incompatible
            // stream, not a fact to skip — folding it silently would advance Version past a dropped
            // change and hand back an aggregate that looks consistent but is not.
            default:
                throw new InvalidOperationException($"Unrecognized segment event type '{@event.GetType()}' on segment {Id}.");
        }

        _version++;
    }
}
