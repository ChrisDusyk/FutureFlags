using FeatureFlags.Domain.Segments;

namespace FeatureFlags.Server.Features.Segments.GetSegmentHistory;

public sealed record GetSegmentHistoryQuery(SegmentKey Key);
