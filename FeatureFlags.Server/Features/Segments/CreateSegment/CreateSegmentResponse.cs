using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Server.Features.Segments.CreateSegment;

public sealed record CreateSegmentConditionResponse(
    string Attribute,
    string Operator,
    IReadOnlyList<AttributeValue> Values);

public sealed record CreateSegmentDefinitionResponse(
    IReadOnlyList<string> IncludedKeys,
    IReadOnlyList<string> ExcludedKeys,
    IReadOnlyList<CreateSegmentConditionResponse> Conditions)
{
    public static CreateSegmentDefinitionResponse From(SegmentDefinition definition) => new(
        definition.IncludedKeys,
        definition.ExcludedKeys,
        [.. definition.Conditions.Select(condition => new CreateSegmentConditionResponse(
            condition.Attribute, condition.Operator.Value, condition.Values))]);
}

public sealed record CreateSegmentResponse(
    Guid Id,
    string Key,
    string Name,
    string Description,
    CreateSegmentDefinitionResponse Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CreateSegmentResponse From(Segment segment) => new(
        segment.Id,
        segment.Key.Value,
        segment.Name,
        segment.Description,
        CreateSegmentDefinitionResponse.From(segment.Definition),
        segment.CreatedAt,
        segment.UpdatedAt);
}
