using FutureFlags.Domain.Segments;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Features.Segments.ListSegments;

/// <summary>
/// Every live segment. No query type and no environment parameter: a segment's definition is global,
/// so unlike a flag listing there is nothing here to scope.
/// </summary>
public sealed class ListSegmentsHandler(ISegmentViewRepository repository)
{
    public async Task<Result<ListSegmentsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var segments = await repository.ListAsync(cancellationToken);

        return Result.Success(new ListSegmentsResponse([.. segments.Select(ListSegmentSummary.From)]));
    }
}
