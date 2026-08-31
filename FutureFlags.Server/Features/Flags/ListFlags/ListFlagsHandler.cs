using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Server.Features.Flags.ListFlags;

public sealed class ListFlagsHandler(IFlagViewRepository repository)
{
    public async Task<Result<ListFlagsResponse>> HandleAsync(
        ListFlagsQuery query,
        CancellationToken cancellationToken = default)
    {
        var flags = await repository.ListAsync(cancellationToken);

        var summaries = flags
            .Select(flag => FlagSummary.From(flag, query.Environment))
            .ToList();

        // An empty list is an answer, not a failure — a console with no flags yet is the ordinary
        // first run, and the screen has copy for it.
        return Result.Success(new ListFlagsResponse(query.Environment.Value, summaries));
    }
}
