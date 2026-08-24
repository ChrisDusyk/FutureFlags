using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Server.Features.Segments.UpdateSegment;

public sealed record UpdateSegmentConditionResponse(
    string Attribute,
    string Operator,
    IReadOnlyList<AttributeValue> Values);

public sealed record UpdateSegmentDefinitionResponse(
    IReadOnlyList<string> IncludedKeys,
    IReadOnlyList<string> ExcludedKeys,
    IReadOnlyList<UpdateSegmentConditionResponse> Conditions)
{
    public static UpdateSegmentDefinitionResponse From(SegmentDefinition definition) => new(
        definition.IncludedKeys,
        definition.ExcludedKeys,
        [.. definition.Conditions.Select(condition => new UpdateSegmentConditionResponse(
            condition.Attribute, condition.Operator.Value, condition.Values))]);
}

public sealed record UpdateSegmentResponse(
    Guid Id,
    string Key,
    string Name,
    string Description,
    UpdateSegmentDefinitionResponse Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static UpdateSegmentResponse From(Segment segment) => new(
        segment.Id,
        segment.Key.Value,
        segment.Name,
        segment.Description,
        UpdateSegmentDefinitionResponse.From(segment.Definition),
        segment.CreatedAt,
        segment.UpdatedAt);
}
