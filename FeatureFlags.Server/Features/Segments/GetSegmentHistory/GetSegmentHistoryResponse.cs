using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Server.Features.Segments.GetSegmentHistory;

public sealed record SegmentHistoryConditionResponse(
    string Attribute,
    string Operator,
    IReadOnlyList<AttributeValue> Values);

public sealed record SegmentHistoryDefinitionResponse(
    IReadOnlyList<string> IncludedKeys,
    IReadOnlyList<string> ExcludedKeys,
    IReadOnlyList<SegmentHistoryConditionResponse> Conditions)
{
    public static SegmentHistoryDefinitionResponse From(SegmentDefinition definition) => new(
        definition.IncludedKeys,
        definition.ExcludedKeys,
        [.. definition.Conditions.Select(condition => new SegmentHistoryConditionResponse(
            condition.Attribute, condition.Operator.Value, condition.Values))]);
}

/// <summary>
/// One entry in a segment's activity log. <see cref="EventType"/> discriminates which of the other
/// fields are set: <see cref="Name"/>/<see cref="Description"/> for "SegmentCreated" and
/// "SegmentDetailsChanged", <see cref="Definition"/> for "SegmentDefinitionChanged", and neither
/// for "SegmentDeleted".
///
/// <para>
/// The definition is carried whole rather than as a diff against the previous entry. Working out
/// what actually changed is the console's job and it has both versions in hand; computing it here
/// would put a rendering decision in the API.
/// </para>
/// </summary>
public sealed record SegmentHistoryEntryResponse(
    string EventType,
    DateTimeOffset OccurredAt,
    string? CausedByName,
    string? Name,
    string? Description,
    SegmentHistoryDefinitionResponse? Definition);

public sealed record GetSegmentHistoryResponse(IReadOnlyList<SegmentHistoryEntryResponse> Entries);
