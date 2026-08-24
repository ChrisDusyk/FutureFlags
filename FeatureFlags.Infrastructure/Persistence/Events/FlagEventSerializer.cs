using System.Text.Json;
using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Flags.Events;
using FeatureFlags.Domain.Segments;

namespace FeatureFlags.Infrastructure.Persistence.Events;

/// <summary>
/// Converts between the domain's <see cref="IFlagEvent"/> types and their persisted
/// <see cref="FlagEventRecord"/> shape. The domain events carry no serialization concern of their
/// own — this is the one place that knows the discriminator strings and payload shapes, including
/// the ones the <c>AddFlagEvents</c> migration's backfill SQL writes directly.
/// </summary>
internal static class FlagEventSerializer
{
    internal const string FlagCreatedEventType = "FlagCreated";
    internal const string FlagStateChangedEventType = "FlagStateChanged";
    internal const string FlagDetailsChangedEventType = "FlagDetailsChanged";
    internal const string FlagTargetingChangedEventType = "FlagTargetingChanged";

    // Case-insensitive so the migration's hand-written backfill JSON doesn't have to match this
    // type's property casing exactly.
    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

    internal static FlagEventRecord ToRecord(Guid flagId, int sequenceNumber, IFlagEvent @event) => @event switch
    {
        FlagCreatedEvent created => new FlagEventRecord
        {
            FlagId = flagId,
            SequenceNumber = sequenceNumber,
            EventType = FlagCreatedEventType,
            Payload = JsonSerializer.Serialize(
                new FlagCreatedPayload(created.Key.Value, created.Name, created.Description), PayloadOptions),
            OccurredAt = created.OccurredAt,
            CausedBy = created.CausedBy,
        },
        FlagStateChangedEvent stateChanged => new FlagEventRecord
        {
            FlagId = flagId,
            SequenceNumber = sequenceNumber,
            EventType = FlagStateChangedEventType,
            Payload = JsonSerializer.Serialize(
                new FlagStateChangedPayload(stateChanged.Environment.Value, stateChanged.IsEnabled), PayloadOptions),
            OccurredAt = stateChanged.OccurredAt,
            CausedBy = stateChanged.CausedBy,
        },
        FlagDetailsChangedEvent detailsChanged => new FlagEventRecord
        {
            FlagId = flagId,
            SequenceNumber = sequenceNumber,
            EventType = FlagDetailsChangedEventType,
            Payload = JsonSerializer.Serialize(
                new FlagDetailsChangedPayload(detailsChanged.Name, detailsChanged.Description), PayloadOptions),
            OccurredAt = detailsChanged.OccurredAt,
            CausedBy = detailsChanged.CausedBy,
        },
        FlagTargetingChangedEvent targetingChanged => new FlagEventRecord
        {
            FlagId = flagId,
            SequenceNumber = sequenceNumber,
            EventType = FlagTargetingChangedEventType,
            Payload = JsonSerializer.Serialize(
                new FlagTargetingChangedPayload(
                    targetingChanged.Environment.Value,
                    [.. targetingChanged.Segments.Select(segment => segment.Value)]),
                PayloadOptions),
            OccurredAt = targetingChanged.OccurredAt,
            CausedBy = targetingChanged.CausedBy,
        },
        _ => throw new InvalidOperationException($"Unrecognized flag event type '{@event.GetType()}'."),
    };

    internal static IFlagEvent ToEvent(FlagEventRecord record) => record.EventType switch
    {
        FlagCreatedEventType => ToFlagCreatedEvent(record),
        FlagStateChangedEventType => ToFlagStateChangedEvent(record),
        FlagDetailsChangedEventType => ToFlagDetailsChangedEvent(record),
        FlagTargetingChangedEventType => ToFlagTargetingChangedEvent(record),
        _ => throw new InvalidOperationException($"Unrecognized flag event type '{record.EventType}' on flag {record.FlagId}."),
    };

    private static FlagCreatedEvent ToFlagCreatedEvent(FlagEventRecord record)
    {
        var payload = JsonSerializer.Deserialize<FlagCreatedPayload>(record.Payload, PayloadOptions)!;
        return new FlagCreatedEvent(record.FlagId, FlagKey.FromPersisted(payload.Key), payload.Name, payload.Description, record.OccurredAt, record.CausedBy);
    }

    private static FlagStateChangedEvent ToFlagStateChangedEvent(FlagEventRecord record)
    {
        var payload = JsonSerializer.Deserialize<FlagStateChangedPayload>(record.Payload, PayloadOptions)!;
        return new FlagStateChangedEvent(record.FlagId, EnvironmentKey.FromPersisted(payload.Environment), payload.IsEnabled, record.OccurredAt, record.CausedBy);
    }

    private static FlagDetailsChangedEvent ToFlagDetailsChangedEvent(FlagEventRecord record)
    {
        var payload = JsonSerializer.Deserialize<FlagDetailsChangedPayload>(record.Payload, PayloadOptions)!;
        return new FlagDetailsChangedEvent(record.FlagId, payload.Name, payload.Description, record.OccurredAt, record.CausedBy);
    }

    private static FlagTargetingChangedEvent ToFlagTargetingChangedEvent(FlagEventRecord record)
    {
        var payload = JsonSerializer.Deserialize<FlagTargetingChangedPayload>(record.Payload, PayloadOptions)!;

        return new FlagTargetingChangedEvent(
            record.FlagId,
            EnvironmentKey.FromPersisted(payload.Environment),
            [.. payload.Segments.Select(SegmentKey.FromPersisted)],
            record.OccurredAt,
            record.CausedBy);
    }

    private sealed record FlagCreatedPayload(string Key, string Name, string Description);

    private sealed record FlagStateChangedPayload(string Environment, bool IsEnabled);

    private sealed record FlagDetailsChangedPayload(string Name, string Description);

    private sealed record FlagTargetingChangedPayload(string Environment, string[] Segments);
}
