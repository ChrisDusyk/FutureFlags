using FeatureFlags.Domain.Segments;

namespace FeatureFlags.Server.Features.Segments.GetSegment;

public sealed record GetSegmentQuery(SegmentKey Key);
