using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Segments.Events;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Infrastructure.Persistence.Events;
using FeatureFlags.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Infrastructure.Persistence.Repositories;

/// <summary>
/// The read side. Pure projection, with no replay except <see cref="GetHistoryAsync"/>.
///
/// <para>
/// Every query here filters tombstones out, so nothing downstream has to remember to. History is
/// the one exception, and deliberately: a retired segment's events stay readable, which is the
/// reason the row is tombstoned rather than deleted in the first place.
/// </para>
/// </summary>
internal sealed class SegmentViewRepository(AppDbContext dbContext) : ISegmentViewRepository
{
    public async Task<IReadOnlyList<SegmentView>> ListAsync(CancellationToken cancellationToken = default) =>
        [.. (await dbContext.SegmentRows
            .AsNoTracking()
            .Where(row => row.DeletedAt == null)
            .OrderBy(row => row.Key)
            .ToListAsync(cancellationToken))
            .Select(ToView)];

    public async Task<Option<SegmentView>> GetByKeyAsync(SegmentKey key, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.SegmentRows
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Key == key && candidate.DeletedAt == null, cancellationToken);

        return row is null ? Option<SegmentView>.None : Option<SegmentView>.Some(ToView(row));
    }

    public async Task<IReadOnlyList<SegmentKey>> FilterExistingAsync(
        IReadOnlyCollection<SegmentKey> keys,
        CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
            return [];

        // Compared as SegmentKey rather than reaching through to its Value: the property goes
        // through a value converter, and EF cannot translate a member access on the far side of one
        // — it produces a runtime "could not be translated" rather than a compile error. Comparing
        // the converted type is what the converter is for, and it is shorter besides.
        var wanted = keys.Distinct().ToList();

        return await dbContext.SegmentRows
            .AsNoTracking()
            .Where(row => row.DeletedAt == null && wanted.Contains(row.Key))
            .Select(row => row.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ISegmentEvent>> GetHistoryAsync(
        Guid segmentId,
        CancellationToken cancellationToken = default)
    {
        var records = await dbContext.SegmentEvents
            .AsNoTracking()
            .Where(record => record.SegmentId == segmentId)
            .OrderByDescending(record => record.SequenceNumber)
            .ToListAsync(cancellationToken);

        return [.. records.Select(SegmentEventSerializer.ToEvent)];
    }

    private static SegmentView ToView(SegmentRow row) => new(
        row.Id,
        row.Key,
        row.Name,
        row.Description,
        row.Definition,
        row.CreatedAt,
        row.UpdatedAt);
}
