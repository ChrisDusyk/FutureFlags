using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Flags.Events;
using FutureFlags.Domain.Segments;
using FutureFlags.Domain.Shared;
using FutureFlags.Infrastructure.Persistence.Events;
using FutureFlags.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;

namespace FutureFlags.Infrastructure.Persistence.Repositories;

/// <summary>The read side — a pure projection query, no event replay.</summary>
internal sealed class FlagViewRepository(AppDbContext dbContext) : IFlagViewRepository
{
    public async Task<IReadOnlyList<FlagView>> ListAsync(CancellationToken cancellationToken = default) =>
        [.. (await dbContext.FlagRows.OrderBy(row => row.Key).ToListAsync(cancellationToken)).Select(ToView)];

    public async Task<Option<FlagView>> GetByKeyAsync(FlagKey key, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.FlagRows
            .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

        return row.ToOption().Map(ToView);
    }

    public async Task<IReadOnlyList<IFlagEvent>> GetHistoryAsync(Guid flagId, CancellationToken cancellationToken = default)
    {
        var records = await dbContext.FlagEvents
            .Where(record => record.FlagId == flagId)
            .OrderByDescending(record => record.SequenceNumber)
            .ToListAsync(cancellationToken);

        return [.. records.Select(FlagEventSerializer.ToEvent)];
    }

    public async Task<IReadOnlyList<FlagTargetingView>> ListTargetingAsync(
        SegmentKey segment,
        CancellationToken cancellationToken = default)
    {
        // The GIN index on TargetedSegments is what makes the containment test cheap; Npgsql
        // translates this to `@>` rather than to a scan-and-filter.
        var rows = await dbContext.FlagRows
            .AsNoTracking()
            .Where(row => row.States.Any(state => state.TargetedSegments.Contains(segment.Value)))
            .OrderBy(row => row.Key)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .SelectMany(row => row.States
                    .Where(state => state.TargetedSegments.Contains(segment.Value))
                    .Select(state => new FlagTargetingView(row.Key, row.Name, state.Environment)))
                .OrderBy(view => view.Key.Value, StringComparer.Ordinal)
                .ThenBy(view => view.Environment.Ordinal),
        ];
    }

    private static FlagView ToView(FlagRow row) => new(
        row.Id,
        row.Key,
        row.Name,
        row.Description,
        row.CreatedAt,
        row.UpdatedAt,
        [.. row.States.Select(state => new FlagStateView(
            state.Environment,
            state.IsEnabled,
            [.. state.TargetedSegments.Select(SegmentKey.FromPersisted)],
            state.UpdatedAt,
            state.OnVariant,
            state.OffVariant))])
    {
        ValueType = row.ValueType,
        Variants = row.Variants,
    };
}
