using System.Diagnostics.CodeAnalysis;
using FeatureFlags.Domain.Segments;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Infrastructure.Persistence.Configurations;
using FeatureFlags.Infrastructure.Persistence.Events;
using FeatureFlags.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FeatureFlags.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write side, on the same terms as <see cref="FeatureFlagRepository"/>: a <see cref="Segment"/>
/// is reconstructed by replaying <c>segment_events</c>, and on save this appends whatever new events
/// a command raised and syncs the corresponding <see cref="SegmentRow"/> from the aggregate's
/// resulting state, in one transaction.
/// </summary>
internal sealed class SegmentRepository(AppDbContext dbContext) : ISegmentRepository
{
    private readonly List<(Segment Segment, SegmentRow Row)> _tracked = [];

    public async Task<Option<Segment>> GetByKeyAsync(SegmentKey key, CancellationToken cancellationToken = default)
    {
        // A repeat read within the same scope returns the same instance rather than rehydrating a
        // second one: two aggregates for one row would each think they own the next sequence
        // number, and saving both would append overlapping ranges — a self-inflicted concurrency
        // conflict rather than a real one.
        var tracked = _tracked.FirstOrDefault(entry => entry.Row.Key == key).Segment;
        if (tracked is not null)
            return Option<Segment>.Some(tracked);

        // Tombstones included, deliberately. A caller creating a segment over a retired key needs a
        // different answer than one creating it over a live key, and this is where it finds out.
        var row = await dbContext.SegmentRows
            .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

        if (row is null)
            return Option<Segment>.None;

        var segment = await RehydrateAsync(row, cancellationToken);
        return Option<Segment>.Some(segment);
    }

    public Task AddAsync(Segment segment, CancellationToken cancellationToken = default)
    {
        var row = ToNewRow(segment);
        dbContext.SegmentRows.Add(row);
        _tracked.Add((segment, row));

        return Task.CompletedTask;
    }

    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (segment, row) in _tracked)
        {
            if (segment.UncommittedEvents.Count == 0)
                continue;

            var startingSequence = segment.Version - segment.UncommittedEvents.Count + 1;
            for (var i = 0; i < segment.UncommittedEvents.Count; i++)
            {
                var record = SegmentEventSerializer.ToRecord(segment.Id, startingSequence + i, segment.UncommittedEvents[i]);
                dbContext.SegmentEvents.Add(record);
            }

            SyncRow(row, segment);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryGetDuplicateKey(exception, out var duplicateKey))
        {
            // Another writer took this key between the caller's check and this insert. The unique
            // index is what actually settles the race.
            dbContext.ChangeTracker.Clear();
            _tracked.Clear();

            return Result.Failure(SegmentErrors.DuplicateKey(duplicateKey));
        }
        catch (DbUpdateException exception) when (IsConcurrencyConflict(exception, out var conflictedKey))
        {
            dbContext.ChangeTracker.Clear();
            _tracked.Clear();

            return Result.Failure(SegmentErrors.ConcurrencyConflict(conflictedKey!));
        }

        foreach (var (segment, _) in _tracked)
            segment.ClearUncommittedEvents();

        return Result.Success();
    }

    /// <summary>
    /// Rehydrates <paramref name="row"/>'s segment and starts tracking it. Callers check the
    /// identity map themselves first — this method only ever adds a new entry, never looks one up —
    /// so it stays the one place <c>_tracked</c> grows.
    /// </summary>
    private async Task<Segment> RehydrateAsync(SegmentRow row, CancellationToken cancellationToken)
    {
        var records = await dbContext.SegmentEvents
            .Where(record => record.SegmentId == row.Id)
            .OrderBy(record => record.SequenceNumber)
            .ToListAsync(cancellationToken);

        var segment = Segment.Rehydrate(row.Id, records.Select(SegmentEventSerializer.ToEvent));
        _tracked.Add((segment, row));

        return segment;
    }

    private static SegmentRow ToNewRow(Segment segment) => new()
    {
        Id = segment.Id,
        Key = segment.Key,
        Name = segment.Name,
        Description = segment.Description,
        Definition = segment.Definition,
        CreatedAt = segment.CreatedAt,
        UpdatedAt = segment.UpdatedAt,
        DeletedAt = segment.DeletedAt.Match(deleted => (DateTimeOffset?)deleted, () => null),
    };

    private static void SyncRow(SegmentRow row, Segment segment)
    {
        row.Name = segment.Name;
        row.Description = segment.Description;
        row.Definition = segment.Definition;
        row.UpdatedAt = segment.UpdatedAt;
        row.DeletedAt = segment.DeletedAt.Match(deleted => (DateTimeOffset?)deleted, () => null);
    }

    private static bool TryGetDuplicateKey(DbUpdateException exception, [NotNullWhen(true)] out SegmentKey? key)
    {
        key = exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: SegmentRowConfiguration.KeyIndexName
        }
            ? exception.Entries
                .Select(entry => entry.Entity)
                .OfType<SegmentRow>()
                .FirstOrDefault()?.Key
            : null;

        return key is not null;
    }

    private bool IsConcurrencyConflict(DbUpdateException exception, [NotNullWhen(true)] out SegmentKey? key)
    {
        key = null;

        if (exception.InnerException is not PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_segment_events" })
            return false;

        var conflictedSegmentId = exception.Entries
            .Select(entry => entry.Entity)
            .OfType<SegmentEventRecord>()
            .FirstOrDefault()?.SegmentId;

        if (conflictedSegmentId is null)
            return false;

        key = _tracked.FirstOrDefault(tracked => tracked.Segment.Id == conflictedSegmentId).Segment?.Key;
        return key is not null;
    }
}
