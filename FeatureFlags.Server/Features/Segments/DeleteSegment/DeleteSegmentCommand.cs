using FeatureFlags.Domain.Segments;

namespace FeatureFlags.Server.Features.Segments.DeleteSegment;

public sealed record DeleteSegmentCommand(SegmentKey Key, Guid CausedBy);
