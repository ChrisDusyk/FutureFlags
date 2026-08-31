using FutureFlags.Domain.Segments;

namespace FutureFlags.Server.Features.Segments.DeleteSegment;

public sealed record DeleteSegmentCommand(SegmentKey Key, Guid CausedBy);
