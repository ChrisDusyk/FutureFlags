using FeatureFlags.Domain.Segments;
using FeatureFlags.Evaluation;
using FeatureFlags.Infrastructure.Persistence.Repositories;

namespace FeatureFlags.Infrastructure.Tests;

/// <summary>
/// The pieces of the segment write side a fake repository cannot prove: that a definition survives
/// a trip through <c>jsonb</c> unchanged, that the unique index and the event stream's primary key
/// really do catch the races the repository claims to translate, and that a tombstone behaves the
/// way the rest of the design assumes.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SegmentRepositoryTests(PostgresFixture postgres)
{
    private static readonly Guid CausedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static SegmentKey NewKey() => SegmentKey.Create($"segment-{Guid.NewGuid():N}").Value;

    private static SegmentCondition Condition(string attribute, string @operator, params AttributeValue[] values) =>
        SegmentCondition.Create(attribute, @operator, values).Value;

    private async Task<Segment> SeedAsync(SegmentKey key, SegmentDefinition definition, CancellationToken cancellationToken)
    {
        await using var context = postgres.NewDbContext();
        var repository = new SegmentRepository(context);

        var segment = Segment.Create(key.Value, "Seeded", "Seeded for a test.", definition, DateTimeOffset.UtcNow, CausedBy).Value;

        await repository.AddAsync(segment, cancellationToken);
        Assert.True((await repository.SaveChangesAsync(cancellationToken)).IsSuccess);

        return segment;
    }

    [Fact]
    public async Task ADefinition_ShouldSurviveAJsonbRoundTripExactly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = NewKey();

        // jsonb does not store text — it re-renders numbers through `numeric` and reorders object
        // keys. These are the values most likely to lose something on the way: the widest double
        // that still round-trips through JavaScript, one needing all 17 significant digits, a
        // negative zero, and a fraction no binary float represents exactly.
        var definition = SegmentDefinition.Create(
            ["user-2", "user-1"],
            ["user-3"],
            [
                Condition("seats", "greater-than-or-equal", AttributeValue.OfNumber(9007199254740991d)),
                Condition("ratio", "less-than", AttributeValue.OfNumber(0.1 + 0.2)),
                Condition("balance", "less-than", AttributeValue.OfNumber(-0.0)),
                Condition("share", "greater-than", AttributeValue.OfNumber(0.1)),
                Condition("plan", "one-of", AttributeValue.OfText("pro"), AttributeValue.OfText("team")),
                Condition("internal", "equals", AttributeValue.OfBoolean(true)),
                Condition("note", "equals", AttributeValue.OfText("a \"quoted\" ünïcode ✓ value")),
            ]).Value;

        await SeedAsync(key, definition, cancellationToken);

        await using var readContext = postgres.NewDbContext();
        var view = await new SegmentViewRepository(readContext).GetByKeyAsync(key, cancellationToken);

        var read = view.Match(found => found, () => throw new InvalidOperationException("Seeded segment missing."));

        // Value equality over the whole definition, which is exactly what the aggregate's
        // idempotence check compares — so this also proves a reload cannot look like an edit.
        Assert.Equal(definition, read.Definition);
    }

    [Fact]
    public async Task ChangingADefinition_ShouldBeNoticedByChangeTracking()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = NewKey();

        await SeedAsync(key, SegmentDefinition.Create(["user-1"], [], []).Value, cancellationToken);

        var replacement = SegmentDefinition.Create(
            ["user-1", "user-2"],
            [],
            [Condition("plan", "equals", AttributeValue.OfText("pro"))]).Value;

        await using (var writeContext = postgres.NewDbContext())
        {
            var repository = new SegmentRepository(writeContext);
            var segment = (await repository.GetByKeyAsync(key, cancellationToken))
                .Match(found => found, () => throw new InvalidOperationException("Seeded segment missing."));

            Assert.True(segment.ChangeDefinition(replacement, DateTimeOffset.UtcNow, CausedBy).IsSuccess);
            Assert.True((await repository.SaveChangesAsync(cancellationToken)).IsSuccess);
        }

        // The definition goes through a value converter, and EF compares converted values by
        // snapshot. Without the ValueComparer on that property the projection would silently keep
        // the old definition while the event stream moved on — green tests, wrong answers.
        await using var readContext = postgres.NewDbContext();
        var read = (await new SegmentViewRepository(readContext).GetByKeyAsync(key, cancellationToken))
            .Match(found => found, () => throw new InvalidOperationException("Seeded segment missing."));

        Assert.Equal(replacement, read.Definition);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTwoWritersEditTheSameSegmentFromTheSameVersion_TheSecondShouldFailWithConcurrencyConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = NewKey();
        var now = DateTimeOffset.UtcNow;

        await SeedAsync(key, SegmentDefinition.Empty, cancellationToken);

        await using var firstContext = postgres.NewDbContext();
        await using var secondContext = postgres.NewDbContext();

        var firstRepository = new SegmentRepository(firstContext);
        var secondRepository = new SegmentRepository(secondContext);

        var first = (await firstRepository.GetByKeyAsync(key, cancellationToken))
            .Match(found => found, () => throw new InvalidOperationException("Seeded segment missing."));
        var second = (await secondRepository.GetByKeyAsync(key, cancellationToken))
            .Match(found => found, () => throw new InvalidOperationException("Seeded segment missing."));

        first.UpdateDetails("First", null, now.AddMinutes(1), CausedBy);
        second.UpdateDetails("Second", null, now.AddMinutes(1), CausedBy);

        Assert.True((await firstRepository.SaveChangesAsync(cancellationToken)).IsSuccess);

        var secondResult = await secondRepository.SaveChangesAsync(cancellationToken);

        Assert.True(secondResult.IsFailure);
        Assert.Equal(SegmentErrors.ConcurrencyConflict(key), secondResult.Error);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTwoWritersCreateTheSameKey_TheSecondShouldFailWithDuplicateKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = NewKey();

        await SeedAsync(key, SegmentDefinition.Empty, cancellationToken);

        await using var context = postgres.NewDbContext();
        var repository = new SegmentRepository(context);
        var duplicate = Segment.Create(key.Value, "Duplicate", null, null, DateTimeOffset.UtcNow, CausedBy).Value;

        await repository.AddAsync(duplicate, cancellationToken);
        var result = await repository.SaveChangesAsync(cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.DuplicateKey(key), result.Error);
    }

    [Fact]
    public async Task ARetiredSegment_ShouldKeepItsKeyAndItsHistoryWhileLeavingTheReadSide()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = NewKey();

        var seeded = await SeedAsync(key, SegmentDefinition.Create(["user-1"], [], []).Value, cancellationToken);

        await using (var writeContext = postgres.NewDbContext())
        {
            var repository = new SegmentRepository(writeContext);
            var segment = (await repository.GetByKeyAsync(key, cancellationToken))
                .Match(found => found, () => throw new InvalidOperationException("Seeded segment missing."));

            Assert.True(segment.Delete(DateTimeOffset.UtcNow, CausedBy).IsSuccess);
            Assert.True((await repository.SaveChangesAsync(cancellationToken)).IsSuccess);
        }

        await using var readContext = postgres.NewDbContext();

        // Gone from every read-side query, so nothing downstream has to remember to filter.
        Assert.True((await new SegmentViewRepository(readContext).GetByKeyAsync(key, cancellationToken)).IsNone);
        Assert.DoesNotContain(
            await new SegmentViewRepository(readContext).ListAsync(cancellationToken),
            view => view.Key == key);
        Assert.Empty(await new SegmentViewRepository(readContext).FilterExistingAsync([key], cancellationToken));

        // Still reachable from the write side, still carrying its history — which is the entire
        // reason the row is tombstoned rather than deleted.
        var stillThere = (await new SegmentRepository(readContext).GetByKeyAsync(key, cancellationToken))
            .Match(found => found, () => throw new InvalidOperationException("Retired segment unreachable."));

        Assert.True(stillThere.IsDeleted);
        Assert.Equal(key, stillThere.Key);
        Assert.NotEmpty(await new SegmentViewRepository(readContext).GetHistoryAsync(seeded.Id, cancellationToken));
    }

    [Fact]
    public async Task ARetiredSegmentsKey_ShouldNotBeReusable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = NewKey();

        await SeedAsync(key, SegmentDefinition.Empty, cancellationToken);

        await using (var deleteContext = postgres.NewDbContext())
        {
            var repository = new SegmentRepository(deleteContext);
            var segment = (await repository.GetByKeyAsync(key, cancellationToken))
                .Match(found => found, () => throw new InvalidOperationException("Seeded segment missing."));

            segment.Delete(DateTimeOffset.UtcNow, CausedBy);
            await repository.SaveChangesAsync(cancellationToken);
        }

        await using var context = postgres.NewDbContext();
        var repositoryAfter = new SegmentRepository(context);
        var reuse = Segment.Create(key.Value, "Reused", null, null, DateTimeOffset.UtcNow, CausedBy).Value;

        await repositoryAfter.AddAsync(reuse, cancellationToken);
        var result = await repositoryAfter.SaveChangesAsync(cancellationToken);

        // The unique index refuses it whatever the tombstone says. A new segment on this key would
        // otherwise get a fresh id whose stream is silently unrelated to the history under it.
        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.DuplicateKey(key), result.Error);
    }

    [Fact]
    public async Task GetByKeyAsync_TwiceInOneScope_ShouldReturnTheSameInstance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = NewKey();

        await SeedAsync(key, SegmentDefinition.Empty, cancellationToken);

        await using var context = postgres.NewDbContext();
        var repository = new SegmentRepository(context);

        var first = (await repository.GetByKeyAsync(key, cancellationToken)).Reduce(() => null!);
        var second = (await repository.GetByKeyAsync(key, cancellationToken)).Reduce(() => null!);

        // Two aggregates for one row would each claim the next sequence number, turning one
        // request's own two reads into a self-inflicted concurrency conflict.
        Assert.Same(first, second);
    }
}
