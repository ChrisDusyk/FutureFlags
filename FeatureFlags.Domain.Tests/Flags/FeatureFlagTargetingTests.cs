using FeatureFlags.Domain.Environments;
using FeatureFlags.Domain.Flags;
using FeatureFlags.Domain.Flags.Events;
using FeatureFlags.Domain.Segments;

namespace FeatureFlags.Domain.Tests.Flags;

/// <summary>
/// Pointing a flag at segments, one environment at a time. Kept apart from
/// <see cref="FeatureFlagTests"/> because it is a distinct fact about a flag rather than more cases
/// for the ones already there.
/// </summary>
public class FeatureFlagTargetingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static SegmentKey Key(string value) => SegmentKey.Create(value).Value;

    private static FeatureFlag Created() =>
        FeatureFlag.Create("new-checkout", "New checkout", null, EnvironmentKey.All, Now, Actor).Value;

    [Fact]
    public void ANewFlag_ShouldTargetNobodyInEveryEnvironment()
    {
        var flag = Created();

        // Empty means everyone, not nobody — a flag that existed before segments did keeps
        // answering exactly as it always did.
        Assert.All(flag.States, state => Assert.Empty(state.TargetedSegments));
    }

    [Fact]
    public void SetTargeting_ShouldRaiseAndAffectOnlyTheEnvironmentNamed()
    {
        var flag = Created();
        var before = flag.UncommittedEvents.Count;

        var result = flag.SetTargeting(EnvironmentKey.Production, [Key("beta-testers")], Now.AddHours(1), Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(before + 1, flag.UncommittedEvents.Count);

        var raised = Assert.IsType<FlagTargetingChangedEvent>(flag.UncommittedEvents[^1]);
        Assert.Equal(EnvironmentKey.Production, raised.Environment);

        Assert.Equal([Key("beta-testers")], flag.StateIn(EnvironmentKey.Production).Reduce(() => null!).TargetedSegments);
        Assert.Empty(flag.StateIn(EnvironmentKey.Development).Reduce(() => null!).TargetedSegments);
        Assert.Empty(flag.StateIn(EnvironmentKey.Staging).Reduce(() => null!).TargetedSegments);
    }

    [Fact]
    public void SetTargeting_ShouldNormalizeOrderAndDuplicates()
    {
        var flag = Created();

        flag.SetTargeting(EnvironmentKey.Development, [Key("staff"), Key("beta-testers"), Key("staff")], Now.AddHours(1), Actor);

        Assert.Equal(
            [Key("beta-testers"), Key("staff")],
            flag.StateIn(EnvironmentKey.Development).Reduce(() => null!).TargetedSegments);
    }

    [Fact]
    public void SetTargeting_WithTheSameSetInADifferentOrder_ShouldRaiseNothing()
    {
        // Targeting is an OR, so order carries no meaning — and a stable order is what keeps a
        // ruleset's ETag still while somebody opens the editor and saves without changing anything.
        var flag = Created();
        flag.SetTargeting(EnvironmentKey.Development, [Key("staff"), Key("beta-testers")], Now.AddHours(1), Actor);
        var before = flag.UncommittedEvents.Count;

        var result = flag.SetTargeting(
            EnvironmentKey.Development,
            [Key("beta-testers"), Key("staff"), Key("beta-testers")],
            Now.AddHours(2),
            Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(before, flag.UncommittedEvents.Count);
        Assert.Equal(Now.AddHours(1), flag.StateIn(EnvironmentKey.Development).Reduce(() => null!).UpdatedAt);
    }

    [Fact]
    public void SetTargeting_ToNothing_ShouldGoBackToReachingEveryone()
    {
        var flag = Created();
        flag.SetTargeting(EnvironmentKey.Development, [Key("staff")], Now.AddHours(1), Actor);

        var result = flag.SetTargeting(EnvironmentKey.Development, [], Now.AddHours(2), Actor);

        Assert.True(result.IsSuccess);
        Assert.Empty(flag.StateIn(EnvironmentKey.Development).Reduce(() => null!).TargetedSegments);
    }

    [Fact]
    public void SetTargeting_ShouldMoveTheEnvironmentsTimestampAndNotTheFlagsOwn()
    {
        var flag = Created();

        flag.SetTargeting(EnvironmentKey.Production, [Key("staff")], Now.AddHours(1), Actor);

        Assert.Equal(Now.AddHours(1), flag.StateIn(EnvironmentKey.Production).Reduce(() => null!).UpdatedAt);
        Assert.Equal(Now, flag.UpdatedAt);
    }

    [Fact]
    public void SetTargeting_WithMoreSegmentsThanTheCap_ShouldFail()
    {
        var flag = Created();
        var tooMany = Enumerable
            .Range(0, FeatureFlag.MaxTargetedSegments + 1)
            .Select(index => Key($"segment-{index}"))
            .ToList();

        var result = flag.SetTargeting(EnvironmentKey.Development, tooMany, Now, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.TooManyTargetedSegments(flag.Key), result.Error);
    }

    [Fact]
    public void SetTargeting_AndSetEnabled_ShouldBeIndependent()
    {
        // Off beats everything, so a flag can carry targeting while switched off and pick it up
        // again when it is switched back on — turning it off must not discard who it was for.
        var flag = Created();
        flag.SetTargeting(EnvironmentKey.Production, [Key("staff")], Now.AddHours(1), Actor);

        flag.SetEnabled(EnvironmentKey.Production, isEnabled: true, Now.AddHours(2), Actor);

        var state = flag.StateIn(EnvironmentKey.Production).Reduce(() => null!);
        Assert.True(state.IsEnabled);
        Assert.Equal([Key("staff")], state.TargetedSegments);
    }

    [Fact]
    public void Rehydrate_ShouldCarryTargetingAcross()
    {
        var original = Created();
        original.SetTargeting(EnvironmentKey.Production, [Key("staff"), Key("beta-testers")], Now.AddHours(1), Actor);
        original.SetEnabled(EnvironmentKey.Production, isEnabled: true, Now.AddHours(2), Actor);

        var replayed = FeatureFlag.Rehydrate(original.Id, original.UncommittedEvents);

        var state = replayed.StateIn(EnvironmentKey.Production).Reduce(() => null!);
        Assert.True(state.IsEnabled);
        Assert.Equal([Key("beta-testers"), Key("staff")], state.TargetedSegments);
        Assert.Equal(original.Version, replayed.Version);
    }

    [Fact]
    public void TwoTargetingEventsWithTheSameSegments_ShouldBeEqual()
    {
        // A record would compare the list by reference, which is harmless in production — nothing
        // compares events — and quietly fatal in a test that believes it is checking one.
        var first = new FlagTargetingChangedEvent(Guid.Empty, EnvironmentKey.Development, [Key("staff")], Now, Actor);
        var second = new FlagTargetingChangedEvent(Guid.Empty, EnvironmentKey.Development, [Key("staff")], Now, Actor);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
