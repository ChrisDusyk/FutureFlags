using FutureFlags.Domain.Flags;
using FutureFlags.Evaluation;

namespace FutureFlags.Server.Features.Flags.CreateFlag;

// Slice-qualified rather than plain FlagStateResponse: AddOpenApi()'s default schema-ID
// generation keys on the bare type name, and this shape is declared once per slice (see
// GetFlagResponse.cs, UpdateFlagResponse.cs) — an unqualified name here would collide with
// theirs and silently collapse to whichever one the generator happened to see first.
public sealed record CreateFlagStateResponse(
    string Environment,
    bool IsEnabled,
    DateTimeOffset UpdatedAt,
    string OnVariant,
    string OffVariant);

/// <summary>One named value the flag can serve. Slice-qualified for the reason above.</summary>
public sealed record CreateFlagVariantResponse(string Name, FlagValue Value);

public sealed record CreateFlagResponse(
    Guid Id,
    string Key,
    string Name,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CreateFlagStateResponse> States,
    string ValueType,
    IReadOnlyList<CreateFlagVariantResponse> Variants)
{
    public static CreateFlagResponse From(FeatureFlag flag) => new(
        flag.Id,
        flag.Key.Value,
        flag.Name,
        flag.Description,
        flag.CreatedAt,
        flag.UpdatedAt,
        [.. flag.States.Select(state => new CreateFlagStateResponse(
            state.Environment.Value, state.IsEnabled, state.UpdatedAt, state.OnVariant, state.OffVariant))],
        flag.ValueType.Value,
        [.. flag.Variants.Variants.Select(variant =>
            new CreateFlagVariantResponse(variant.Name, variant.Value))]);
}
