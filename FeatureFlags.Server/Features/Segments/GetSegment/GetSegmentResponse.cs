using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Server.Features.Segments.GetSegment;

public sealed record GetSegmentConditionResponse(
    string Attribute,
    string Operator,
    IReadOnlyList<AttributeValue> Values);

public sealed record GetSegmentDefinitionResponse(
    IReadOnlyList<string> IncludedKeys,
    IReadOnlyList<string> ExcludedKeys,
    IReadOnlyList<GetSegmentConditionResponse> Conditions)
{
    public static GetSegmentDefinitionResponse From(SegmentDefinition definition) => new(
        definition.IncludedKeys,
        definition.ExcludedKeys,
        [.. definition.Conditions.Select(condition => new GetSegmentConditionResponse(
            condition.Attribute, condition.Operator.Value, condition.Values))]);
}

/// <summary>One flag and environment that names this segment. See <see cref="FlagTargetingView"/>.</summary>
public sealed record GetSegmentDependentResponse(string FlagKey, string FlagName, string Environment);

/// <summary>
/// One segment, whole. <c>TargetedBy</c> is everywhere it is currently holding something up:
/// editing the definition changes all of those at once, and it cannot be deleted while that list
/// is non-empty.
/// </summary>
public sealed record GetSegmentResponse(
    Guid Id,
    string Key,
    string Name,
    string Description,
    GetSegmentDefinitionResponse Definition,
    IReadOnlyList<GetSegmentDependentResponse> TargetedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static GetSegmentResponse From(SegmentView segment, IReadOnlyList<FlagTargetingView> targeting) => new(
        segment.Id,
        segment.Key.Value,
        segment.Name,
        segment.Description,
        GetSegmentDefinitionResponse.From(segment.Definition),
        [.. targeting.Select(view => new GetSegmentDependentResponse(
            view.Key.Value, view.Name, view.Environment.Value))],
        segment.CreatedAt,
        segment.UpdatedAt);
}
