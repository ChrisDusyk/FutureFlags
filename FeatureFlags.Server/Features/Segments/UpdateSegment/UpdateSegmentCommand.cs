using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Server.Features.Segments.UpdateSegment;

/// <summary>
/// The details and the definition together, in one PUT. They are edited on one screen and saved by
/// one button, and splitting them would mean a half-applied edit is representable.
/// </summary>
public sealed record UpdateSegmentCommand(
    SegmentKey Key,
    string? Name,
    string? Description,
    SegmentDefinition Definition,
    Guid CausedBy);

/// <summary>The wire shape. <see cref="UpdateSegmentCommand"/> is what survives validation.</summary>
public sealed record UpdateSegmentRequest(
    string? Name,
    string? Description,
    UpdateSegmentDefinitionRequest? Definition);

public sealed record UpdateSegmentDefinitionRequest(
    IReadOnlyList<string>? IncludedKeys,
    IReadOnlyList<string>? ExcludedKeys,
    IReadOnlyList<UpdateSegmentConditionRequest>? Conditions);

/// <summary>See <see cref="AttributeValueJsonConverter"/> — values are bare JSON primitives.</summary>
public sealed record UpdateSegmentConditionRequest(
    string? Attribute,
    string? Operator,
    IReadOnlyList<AttributeValue>? Values);
