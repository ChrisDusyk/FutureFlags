using FeatureFlags.Domain.Flags;

namespace FeatureFlags.Server.Features.Flags.GetFlag;

// Slice-qualified rather than plain FlagStateResponse: AddOpenApi()'s default schema-ID
// generation keys on the bare type name, and this shape is declared once per slice (see
// CreateFlagResponse.cs, UpdateFlagResponse.cs) — an unqualified name here would collide with
// theirs and silently collapse to whichever one the generator happened to see first.
public sealed record GetFlagStateResponse(
    string Environment,
    bool IsEnabled,
    IReadOnlyList<string> TargetedSegments,
    DateTimeOffset UpdatedAt);

public sealed record GetFlagResponse(
    Guid Id,
    string Key,
    string Name,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<GetFlagStateResponse> States)
{
    public static GetFlagResponse From(FlagView flag) => new(
        flag.Id,
        flag.Key.Value,
        flag.Name,
        flag.Description,
        flag.CreatedAt,
        flag.UpdatedAt,
        [.. flag.States.Select(state =>
            new GetFlagStateResponse(
                state.Environment.Value,
                state.IsEnabled,
                [.. state.TargetedSegments.Select(segment => segment.Value)],
                state.UpdatedAt))]);
}
