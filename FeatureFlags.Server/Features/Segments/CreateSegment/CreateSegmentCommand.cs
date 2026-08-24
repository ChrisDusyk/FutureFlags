using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;

namespace FeatureFlags.Server.Features.Segments.CreateSegment;

public sealed record CreateSegmentCommand(
    string? Key,
    string? Name,
    string? Description,
    SegmentDefinition Definition,
    Guid CausedBy);

/// <summary>The wire shape. <see cref="CreateSegmentCommand"/> is what survives validation.</summary>
public sealed record CreateSegmentRequest(
    string? Key,
    string? Name,
    string? Description,
    CreateSegmentDefinitionRequest? Definition);

/// <summary>
/// Slice-qualified rather than a plain <c>SegmentDefinitionRequest</c>, the same guard
/// <c>CreateFlagResponse</c> documents: <c>AddOpenApi()</c> keys schema IDs on the bare type name,
/// so an unqualified name here would collide with the update slice's.
/// </summary>
public sealed record CreateSegmentDefinitionRequest(
    IReadOnlyList<string>? IncludedKeys,
    IReadOnlyList<string>? ExcludedKeys,
    IReadOnlyList<CreateSegmentConditionRequest>? Conditions);

/// <summary>
/// <see cref="Values"/> arrives as bare JSON primitives — <c>"pro"</c>, <c>47</c>, <c>true</c> —
/// because JSON's own types are exactly the three an attribute can hold. See
/// <see cref="AttributeValueJsonConverter"/>, declared directly on <see cref="AttributeValue"/>
/// via <c>[JsonConverter]</c> rather than registered anywhere — that is what lets model binding, a
/// ruleset payload, and everything else that serializes one agree without each remembering to opt in.
/// </summary>
public sealed record CreateSegmentConditionRequest(
    string? Attribute,
    string? Operator,
    IReadOnlyList<AttributeValue>? Values);
