using System.Text.Json;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Segments.Events;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Infrastructure.Persistence.Events;

/// <summary>
/// Converts between the domain's <see cref="ISegmentEvent"/> types and their persisted
/// <see cref="SegmentEventRecord"/> shape.
///
/// <para>
/// A definition's values serialize as bare JSON primitives, through
/// <see cref="AttributeValueJsonConverter"/> — the same converter and the same shape a ruleset uses
/// on the wire. One rendering rather than two is the point: a stored condition and a shipped one
/// are the same bytes, so there is no second place for a number to lose a digit.
/// </para>
/// <para>
/// Note that <c>jsonb</c> normalizes what it stores — it reorders object keys and re-renders
/// numbers through <c>numeric</c> — so the text read back is not byte-identical to the text written.
/// That is harmless only for as long as nothing hashes the stored payload; the ruleset ETag is
/// computed from the projected definition, not from this.
/// </para>
/// </summary>
internal static class SegmentEventSerializer
{
    internal const string SegmentCreatedEventType = "SegmentCreated";
    internal const string SegmentDetailsChangedEventType = "SegmentDetailsChanged";
    internal const string SegmentDefinitionChangedEventType = "SegmentDefinitionChanged";
    internal const string SegmentDeletedEventType = "SegmentDeleted";

    private static readonly JsonSerializerOptions PayloadOptions = BuildPayloadOptions();

    internal static SegmentEventRecord ToRecord(Guid segmentId, int sequenceNumber, ISegmentEvent @event) => @event switch
    {
        SegmentCreatedEvent created => Record(
            segmentId, sequenceNumber, SegmentCreatedEventType, created.OccurredAt, created.CausedBy,
            new SegmentCreatedPayload(created.Key.Value, created.Name, created.Description)),

        SegmentDetailsChangedEvent detailsChanged => Record(
            segmentId, sequenceNumber, SegmentDetailsChangedEventType, detailsChanged.OccurredAt, detailsChanged.CausedBy,
            new SegmentDetailsChangedPayload(detailsChanged.Name, detailsChanged.Description)),

        SegmentDefinitionChangedEvent definitionChanged => Record(
            segmentId, sequenceNumber, SegmentDefinitionChangedEventType, definitionChanged.OccurredAt, definitionChanged.CausedBy,
            ToPayload(definitionChanged.Definition)),

        // No payload of its own: when it happened and who did it are already columns.
        SegmentDeletedEvent deleted => Record(
            segmentId, sequenceNumber, SegmentDeletedEventType, deleted.OccurredAt, deleted.CausedBy,
            new SegmentDeletedPayload()),

        _ => throw new InvalidOperationException($"Unrecognized segment event type '{@event.GetType()}'."),
    };

    internal static ISegmentEvent ToEvent(SegmentEventRecord record) => record.EventType switch
    {
        SegmentCreatedEventType => ToSegmentCreatedEvent(record),
        SegmentDetailsChangedEventType => ToSegmentDetailsChangedEvent(record),
        SegmentDefinitionChangedEventType => ToSegmentDefinitionChangedEvent(record),
        SegmentDeletedEventType => new SegmentDeletedEvent(record.SegmentId, record.OccurredAt, record.CausedBy),
        _ => throw new InvalidOperationException(
            $"Unrecognized segment event type '{record.EventType}' on segment {record.SegmentId}."),
    };

    /// <summary>
    /// The definition as it is stored, and as <see cref="SegmentDefinition"/> is rebuilt from it.
    /// Public to the assembly because the read-model row stores the very same shape — a projection
    /// and an event payload describing the same definition differently would be two things to keep
    /// in step for no gain.
    /// </summary>
    internal static SegmentDefinitionPayload ToPayload(SegmentDefinition definition) => new(
        [.. definition.IncludedKeys],
        [.. definition.ExcludedKeys],
        [.. definition.Conditions.Select(condition => new SegmentConditionPayload(
            condition.Attribute,
            condition.Operator.Value,
            [.. condition.Values]))]);

    /// <summary>
    /// Rebuilds a definition from storage without revalidating it. Deliberately through
    /// <c>FromPersisted</c>: a condition whose operator or cap this build would now refuse still has
    /// to be readable, because refusing it here would make a replay throw rather than tell the truth
    /// about what somebody once wrote.
    /// </summary>
    internal static SegmentDefinition ToDefinition(SegmentDefinitionPayload payload) => SegmentDefinition.FromPersisted(
        payload.Included,
        payload.Excluded,
        [.. payload.Conditions.Select(condition => SegmentCondition.FromPersisted(
            condition.Attribute,
            ConditionOperator.FromPersisted(condition.Operator),
            condition.Values))]);

    internal static string SerializeDefinition(SegmentDefinition definition) =>
        JsonSerializer.Serialize(ToPayload(definition), PayloadOptions);

    internal static SegmentDefinition DeserializeDefinition(string json) =>
        ToDefinition(JsonSerializer.Deserialize<SegmentDefinitionPayload>(json, PayloadOptions)!);

    private static SegmentEventRecord Record<TPayload>(
        Guid segmentId,
        int sequenceNumber,
        string eventType,
        DateTimeOffset occurredAt,
        Guid? causedBy,
        TPayload payload) => new()
        {
            SegmentId = segmentId,
            SequenceNumber = sequenceNumber,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload, PayloadOptions),
            OccurredAt = occurredAt,
            CausedBy = causedBy,
        };

    private static SegmentCreatedEvent ToSegmentCreatedEvent(SegmentEventRecord record)
    {
        var payload = JsonSerializer.Deserialize<SegmentCreatedPayload>(record.Payload, PayloadOptions)!;

        return new SegmentCreatedEvent(
            record.SegmentId,
            SegmentKey.FromPersisted(payload.Key),
            payload.Name,
            payload.Description,
            record.OccurredAt,
            record.CausedBy);
    }

    private static SegmentDetailsChangedEvent ToSegmentDetailsChangedEvent(SegmentEventRecord record)
    {
        var payload = JsonSerializer.Deserialize<SegmentDetailsChangedPayload>(record.Payload, PayloadOptions)!;

        return new SegmentDetailsChangedEvent(
            record.SegmentId, payload.Name, payload.Description, record.OccurredAt, record.CausedBy);
    }

    private static SegmentDefinitionChangedEvent ToSegmentDefinitionChangedEvent(SegmentEventRecord record)
    {
        var payload = JsonSerializer.Deserialize<SegmentDefinitionPayload>(record.Payload, PayloadOptions)!;

        return new SegmentDefinitionChangedEvent(
            record.SegmentId, ToDefinition(payload), record.OccurredAt, record.CausedBy);
    }

    private static JsonSerializerOptions BuildPayloadOptions()
    {
        // Case-insensitive for the same reason the flag serializer is: hand-written JSON in a
        // migration should not have to match a C# property's casing exactly. AttributeValue brings
        // its own converter as an attribute, so there is nothing else to register.
        return new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    internal sealed record SegmentDefinitionPayload(
        string[] Included,
        string[] Excluded,
        SegmentConditionPayload[] Conditions);

    internal sealed record SegmentConditionPayload(
        string Attribute,
        string Operator,
        AttributeValue[] Values);

    private sealed record SegmentCreatedPayload(string Key, string Name, string Description);

    private sealed record SegmentDetailsChangedPayload(string Name, string Description);

    private sealed record SegmentDeletedPayload;
}
