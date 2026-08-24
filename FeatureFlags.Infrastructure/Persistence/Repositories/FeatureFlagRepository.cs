using System.Diagnostics.CodeAnalysis;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Flags.Events;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Infrastructure.Persistence.Configurations;
using FeatureFlags.Infrastructure.Persistence.Events;
using FeatureFlags.Infrastructure.Persistence.ReadModels;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FeatureFlags.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write side. <see cref="FeatureFlag"/> is no longer EF-tracked directly — this repository
/// reconstructs it by replaying <c>flag_events</c>, and on save appends whatever new events a
/// command raised and syncs the corresponding <see cref="FlagRow"/> from the aggregate's resulting
/// state, in one transaction.
/// </summary>
internal sealed class FeatureFlagRepository(AppDbContext dbContext) : IFeatureFlagRepository
{
    private readonly List<(FeatureFlag Flag, FlagRow Row)> _tracked = [];

    public async Task<Option<FeatureFlag>> GetByKeyAsync(FlagKey key, CancellationToken cancellationToken = default)
    {
        // A repeat read within the same scope returns the same instance rather than rehydrating a
        // second one: two aggregates for one row would each think they own the next sequence
        // number, and saving both would append overlapping (FlagId, SequenceNumber) ranges — a
        // self-inflicted concurrency conflict, not a real one.
        var tracked = _tracked.FirstOrDefault(entry => entry.Row.Key == key).Flag;
        if (tracked is not null)
            return Option<FeatureFlag>.Some(tracked);

        var row = await dbContext.FlagRows
            .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

        if (row is null)
            return Option<FeatureFlag>.None;

        var flag = await RehydrateAsync(row, cancellationToken);
        return Option<FeatureFlag>.Some(flag);
    }

    public Task AddAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
    {
        var row = ToNewRow(flag);
        dbContext.FlagRows.Add(row);
        _tracked.Add((flag, row));

        return Task.CompletedTask;
    }

    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (flag, row) in _tracked)
        {
            if (flag.UncommittedEvents.Count == 0)
                continue;

            var startingSequence = flag.Version - flag.UncommittedEvents.Count + 1;
            for (var i = 0; i < flag.UncommittedEvents.Count; i++)
            {
                var record = FlagEventSerializer.ToRecord(flag.Id, startingSequence + i, flag.UncommittedEvents[i]);
                dbContext.FlagEvents.Add(record);
            }

            SyncRow(row, flag);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryGetDuplicateKey(exception, out var duplicateKey))
        {
            // Another writer took this key between the caller's check and this insert. The unique
            // index is what actually settles the race; translating it keeps the outcome a Conflict
            // rather than an unhandled exception.
            dbContext.ChangeTracker.Clear();
            _tracked.Clear();

            return Result.Failure(FlagErrors.DuplicateKey(duplicateKey));
        }
        catch (DbUpdateException exception) when (IsConcurrencyConflict(exception, out var conflictedKey))
        {
            dbContext.ChangeTracker.Clear();
            _tracked.Clear();

            return Result.Failure(FlagErrors.ConcurrencyConflict(conflictedKey!));
        }

        foreach (var (flag, _) in _tracked)
            flag.ClearUncommittedEvents();

        return Result.Success();
    }

    /// <summary>
    /// Rehydrates <paramref name="row"/>'s flag and starts tracking it. Callers check the identity
    /// map themselves first — this method only ever adds a new entry, never looks one up — so it
    /// stays the one place <c>_tracked</c> grows.
    /// </summary>
    private async Task<FeatureFlag> RehydrateAsync(FlagRow row, CancellationToken cancellationToken)
    {
        var records = await dbContext.FlagEvents
            .Where(record => record.FlagId == row.Id)
            .OrderBy(record => record.SequenceNumber)
            .ToListAsync(cancellationToken);

        var flag = FeatureFlag.Rehydrate(row.Id, records.Select(FlagEventSerializer.ToEvent));
        _tracked.Add((flag, row));

        return flag;
    }

    private static FlagRow ToNewRow(FeatureFlag flag) => new()
    {
        Id = flag.Id,
        Key = flag.Key,
        Name = flag.Name,
        Description = flag.Description,
        CreatedAt = flag.CreatedAt,
        UpdatedAt = flag.UpdatedAt,
        States = [.. flag.States.Select(state => new FlagStateRow
        {
            Environment = state.Environment,
            IsEnabled = state.IsEnabled,
            TargetedSegments = [.. state.TargetedSegments.Select(segment => segment.Value)],
            UpdatedAt = state.UpdatedAt,
        })],
    };

    private static void SyncRow(FlagRow row, FeatureFlag flag)
    {
        row.Name = flag.Name;
        row.Description = flag.Description;
        row.UpdatedAt = flag.UpdatedAt;

        foreach (var state in flag.States)
        {
            var stateRow = row.States.FirstOrDefault(candidate => candidate.Environment == state.Environment);
            if (stateRow is null)
                row.States.Add(new FlagStateRow
                {
                    Environment = state.Environment,
                    IsEnabled = state.IsEnabled,
                    TargetedSegments = [.. state.TargetedSegments.Select(segment => segment.Value)],
                    UpdatedAt = state.UpdatedAt,
                });
            else
            {
                stateRow.IsEnabled = state.IsEnabled;
                // A fresh list rather than clearing and refilling the existing one: EF snapshots a
                // collection, and mutating the instance it snapshotted is the classic way to make a
                // change invisible to change tracking.
                stateRow.TargetedSegments = [.. state.TargetedSegments.Select(segment => segment.Value)];
                stateRow.UpdatedAt = state.UpdatedAt;
            }
        }
    }

    private static bool TryGetDuplicateKey(DbUpdateException exception, [NotNullWhen(true)] out FlagKey? key)
    {
        key = exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: FlagRowConfiguration.KeyIndexName
        }
            ? exception.Entries
                .Select(entry => entry.Entity)
                .OfType<FlagRow>()
                .FirstOrDefault()?.Key
            : null;

        return key is not null;
    }

    private bool IsConcurrencyConflict(DbUpdateException exception, [NotNullWhen(true)] out FlagKey? key)
    {
        key = null;

        if (exception.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_flag_events" })
            return false;

        var conflictedFlagId = exception.Entries
            .Select(entry => entry.Entity)
            .OfType<FlagEventRecord>()
            .FirstOrDefault()?.FlagId;

        if (conflictedFlagId is null)
            return false;

        key = _tracked.FirstOrDefault(tracked => tracked.Flag.Id == conflictedFlagId).Flag?.Key;
        return key is not null;
    }
}
