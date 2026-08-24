namespace FeatureFlags.Server.Features.Flags.GetFlagHistory;

/// <summary>
/// One entry in a flag's activity log. <see cref="EventType"/> discriminates which of the other
/// fields are set: <see cref="Name"/>/<see cref="Description"/> for "FlagCreated" and
/// "FlagDetailsChanged", <see cref="Environment"/>/<see cref="IsEnabled"/> for "FlagStateChanged",
/// and <see cref="Environment"/>/<see cref="TargetedSegments"/> for "FlagTargetingChanged".
/// </summary>
public sealed record FlagHistoryEntryResponse(
    string EventType,
    DateTimeOffset OccurredAt,
    string? CausedByName,
    string? Name,
    string? Description,
    string? Environment,
    bool? IsEnabled,
    IReadOnlyList<string>? TargetedSegments = null);

public sealed record GetFlagHistoryResponse(IReadOnlyList<FlagHistoryEntryResponse> Entries);
