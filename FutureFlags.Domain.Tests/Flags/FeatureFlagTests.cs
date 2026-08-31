using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Flags.Events;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Tests.Flags;

public class FeatureFlagTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CausedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly EnvironmentKey[] Nowhere = [];

    [Fact]
    public void Create_WithValidInput_ShouldSucceed()
    {
        var result = FeatureFlag.Create(
            "new-checkout",
            "New checkout",
            "Rolls out the rewritten checkout.",
            [EnvironmentKey.Development],
            Now,
            CausedBy);

        Assert.True(result.IsSuccess);

        var flag = result.Value;
        Assert.Equal("new-checkout", flag.Key.Value);
        Assert.Equal("New checkout", flag.Name);
        Assert.Equal("Rolls out the rewritten checkout.", flag.Description);
        Assert.Equal(Now, flag.CreatedAt);
        Assert.Equal(Now, flag.UpdatedAt);
        Assert.NotEqual(Guid.Empty, flag.Id);
    }

    [Fact]
    public void Create_ShouldGiveTheFlagAStateInEveryEnvironment()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        Assert.Equal(EnvironmentKey.All.Count, flag.States.Count);
        Assert.Equal(
            EnvironmentKey.All,
            [.. flag.States.Select(state => state.Environment)]);
    }

    [Fact]
    public void Create_ShouldEnableOnlyTheEnvironmentsAskedFor()
    {
        var flag = FeatureFlag.Create(
            "new-checkout",
            "New checkout",
            null,
            [EnvironmentKey.Development, EnvironmentKey.Staging],
            Now,
            CausedBy).Value;

        Assert.True(flag.IsEnabledIn(EnvironmentKey.Development));
        Assert.True(flag.IsEnabledIn(EnvironmentKey.Staging));
        Assert.False(flag.IsEnabledIn(EnvironmentKey.Production));
    }

    [Fact]
    public void Create_WithNoEnvironments_ShouldStartOffEverywhere()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        Assert.All(flag.States, state => Assert.False(state.IsEnabled));
    }

    [Fact]
    public void Create_ShouldStampEveryStateWithTheSameTimestamp()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, [EnvironmentKey.Production], Now, CausedBy).Value;

        Assert.All(flag.States, state => Assert.Equal(Now, state.UpdatedAt));
    }

    [Fact]
    public void Create_WithARepeatedEnvironment_ShouldStillProduceOneStateEach()
    {
        var flag = FeatureFlag.Create(
            "new-checkout",
            "New checkout",
            null,
            [EnvironmentKey.Development, EnvironmentKey.Development],
            Now,
            CausedBy).Value;

        Assert.Equal(EnvironmentKey.All.Count, flag.States.Count);
        Assert.True(flag.IsEnabledIn(EnvironmentKey.Development));
    }

    [Fact]
    public void Create_ShouldAssignUniqueIds()
    {
        var first = FeatureFlag.Create("first", "First", null, Nowhere, Now, CausedBy).Value;
        var second = FeatureFlag.Create("second", "Second", null, Nowhere, Now, CausedBy).Value;

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Create_WithNullDescription_ShouldDefaultToEmpty()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        Assert.Equal(string.Empty, flag.Description);
    }

    [Fact]
    public void Create_ShouldTrimNameAndDescription()
    {
        var flag = FeatureFlag.Create("new-checkout", "  New checkout  ", "  Notes  ", Nowhere, Now, CausedBy).Value;

        Assert.Equal("New checkout", flag.Name);
        Assert.Equal("Notes", flag.Description);
    }

    [Fact]
    public void Create_WithInvalidKey_ShouldPropagateKeyError()
    {
        var result = FeatureFlag.Create("Not A Key", "New checkout", null, Nowhere, Now, CausedBy);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.KeyInvalidFormat, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingName_ShouldFail(string? name)
    {
        var result = FeatureFlag.Create("new-checkout", name, null, Nowhere, Now, CausedBy);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.NameRequired, result.Error);
    }

    [Fact]
    public void Create_WithOverlongName_ShouldFail()
    {
        var result = FeatureFlag.Create(
            "new-checkout",
            new string('a', FeatureFlag.MaxNameLength + 1),
            null,
            Nowhere,
            Now,
            CausedBy);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.NameTooLong, result.Error);
    }

    [Fact]
    public void Create_WithOverlongDescription_ShouldFail()
    {
        var result = FeatureFlag.Create(
            "new-checkout",
            "New checkout",
            new string('a', FeatureFlag.MaxDescriptionLength + 1),
            Nowhere,
            Now,
            CausedBy);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.DescriptionTooLong, result.Error);
    }

    [Fact]
    public void Create_ShouldValidateKeyBeforeName()
    {
        // Both are invalid; the key error wins so callers see a stable first failure.
        var result = FeatureFlag.Create("Not A Key", "", null, Nowhere, Now, CausedBy);

        Assert.Equal(FlagErrors.KeyInvalidFormat, result.Error);
    }

    [Fact]
    public void StateIn_ShouldReturnTheStateForThatEnvironment()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, [EnvironmentKey.Staging], Now, CausedBy).Value;

        var state = flag.StateIn(EnvironmentKey.Staging);

        Assert.True(state.IsSome);
        Assert.True(state.Match(found => found.IsEnabled, () => false));
    }

    [Fact]
    public void SetEnabled_WhenOff_ShouldTurnOnAndStampThatStateOnly()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;
        var later = Now.AddHours(1);

        var result = flag.SetEnabled(EnvironmentKey.Production, isEnabled: true, later, CausedBy);

        Assert.True(result.IsSuccess);
        Assert.True(flag.IsEnabledIn(EnvironmentKey.Production));
        Assert.Equal(later, flag.StateIn(EnvironmentKey.Production).Match(state => state.UpdatedAt, () => default));
    }

    [Fact]
    public void SetEnabled_ShouldLeaveEveryOtherEnvironmentAlone()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        flag.SetEnabled(EnvironmentKey.Production, isEnabled: true, Now.AddHours(1), CausedBy);

        Assert.False(flag.IsEnabledIn(EnvironmentKey.Development));
        Assert.False(flag.IsEnabledIn(EnvironmentKey.Staging));
        Assert.Equal(Now, flag.StateIn(EnvironmentKey.Development).Match(state => state.UpdatedAt, () => default));
    }

    [Fact]
    public void SetEnabled_ShouldNotTouchTheFlagsOwnUpdatedAt()
    {
        // The flag's timestamp answers "when did this flag change", which a toggle in one
        // environment does not. Otherwise every view would report a change it did not have.
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        flag.SetEnabled(EnvironmentKey.Production, isEnabled: true, Now.AddHours(1), CausedBy);

        Assert.Equal(Now, flag.UpdatedAt);
    }

    [Fact]
    public void SetEnabled_WhenAlreadyInThatState_ShouldNotTouchUpdatedAt()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, [EnvironmentKey.Development], Now, CausedBy).Value;

        var result = flag.SetEnabled(EnvironmentKey.Development, isEnabled: true, Now.AddHours(1), CausedBy);

        Assert.True(result.IsSuccess);
        Assert.True(flag.IsEnabledIn(EnvironmentKey.Development));
        Assert.Equal(Now, flag.StateIn(EnvironmentKey.Development).Match(state => state.UpdatedAt, () => default));
    }

    [Fact]
    public void SetEnabled_TurningOff_ShouldDisableAndStamp()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, [EnvironmentKey.Development], Now, CausedBy).Value;
        var later = Now.AddHours(1);

        flag.SetEnabled(EnvironmentKey.Development, isEnabled: false, later, CausedBy);

        Assert.False(flag.IsEnabledIn(EnvironmentKey.Development));
        Assert.Equal(later, flag.StateIn(EnvironmentKey.Development).Match(state => state.UpdatedAt, () => default));
    }

    [Fact]
    public void Create_ShouldRaiseOneFlagCreatedEventFollowedByOneFlagStateChangedEventPerEnvironment()
    {
        var flag = FeatureFlag.Create(
            "new-checkout",
            "New checkout",
            "Rolls out the rewritten checkout.",
            [EnvironmentKey.Staging],
            Now,
            CausedBy).Value;

        Assert.Equal(1 + EnvironmentKey.All.Count, flag.UncommittedEvents.Count);

        var created = Assert.IsType<FlagCreatedEvent>(flag.UncommittedEvents[0]);
        Assert.Equal(flag.Id, created.FlagId);
        Assert.Equal("new-checkout", created.Key.Value);
        Assert.Equal("New checkout", created.Name);
        Assert.Equal("Rolls out the rewritten checkout.", created.Description);
        Assert.Equal(Now, created.OccurredAt);
        Assert.Equal(CausedBy, created.CausedBy);

        var stateEvents = flag.UncommittedEvents.Skip(1).Cast<FlagStateChangedEvent>().ToList();
        Assert.Equal(EnvironmentKey.All, [.. stateEvents.Select(e => e.Environment)]);
        Assert.All(stateEvents, e => Assert.Equal(Now, e.OccurredAt));
        Assert.All(stateEvents, e => Assert.Equal(CausedBy, e.CausedBy));
        Assert.True(stateEvents.Single(e => e.Environment == EnvironmentKey.Staging).IsEnabled);
        Assert.False(stateEvents.Single(e => e.Environment == EnvironmentKey.Development).IsEnabled);
    }

    [Fact]
    public void Create_ShouldSetVersionToTheNumberOfEventsRaised()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        Assert.Equal(1 + EnvironmentKey.All.Count, flag.Version);
    }

    [Fact]
    public void SetEnabled_WhenValueChanges_ShouldRaiseExactlyOneEventAndIncrementVersion()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;
        var eventCountAfterCreate = flag.UncommittedEvents.Count;
        var versionAfterCreate = flag.Version;

        flag.SetEnabled(EnvironmentKey.Production, isEnabled: true, Now.AddHours(1), CausedBy);

        Assert.Equal(eventCountAfterCreate + 1, flag.UncommittedEvents.Count);
        var stateChanged = Assert.IsType<FlagStateChangedEvent>(flag.UncommittedEvents[^1]);
        Assert.Equal(EnvironmentKey.Production, stateChanged.Environment);
        Assert.True(stateChanged.IsEnabled);
        Assert.Equal(CausedBy, stateChanged.CausedBy);
        Assert.Equal(versionAfterCreate + 1, flag.Version);
    }

    [Fact]
    public void SetEnabled_WhenAlreadyInThatState_ShouldRaiseNoEventAndLeaveVersionUnchanged()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, [EnvironmentKey.Development], Now, CausedBy).Value;
        var eventCountAfterCreate = flag.UncommittedEvents.Count;
        var versionAfterCreate = flag.Version;

        var result = flag.SetEnabled(EnvironmentKey.Development, isEnabled: true, Now.AddHours(1), CausedBy);

        Assert.True(result.IsSuccess);
        Assert.Equal(eventCountAfterCreate, flag.UncommittedEvents.Count);
        Assert.Equal(versionAfterCreate, flag.Version);
    }

    [Fact]
    public void UpdateDetails_WithValidInput_ShouldUpdateNameDescriptionAndUpdatedAt()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", "Old description.", Nowhere, Now, CausedBy).Value;
        var later = Now.AddHours(1);

        var result = flag.UpdateDetails("Renamed checkout", "New description.", later, CausedBy);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed checkout", flag.Name);
        Assert.Equal("New description.", flag.Description);
        Assert.Equal(later, flag.UpdatedAt);
    }

    [Fact]
    public void UpdateDetails_ShouldTrimNameAndDescription()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        flag.UpdateDetails("  Renamed  ", "  Notes  ", Now.AddHours(1), CausedBy);

        Assert.Equal("Renamed", flag.Name);
        Assert.Equal("Notes", flag.Description);
    }

    [Fact]
    public void UpdateDetails_WithNullDescription_ShouldDefaultToEmpty()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", "Old description.", Nowhere, Now, CausedBy).Value;

        flag.UpdateDetails("Renamed", null, Now.AddHours(1), CausedBy);

        Assert.Equal(string.Empty, flag.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDetails_WithMissingName_ShouldFailAndLeaveTheFlagUnchanged(string? name)
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", "Old description.", Nowhere, Now, CausedBy).Value;

        var result = flag.UpdateDetails(name, "New description.", Now.AddHours(1), CausedBy);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.NameRequired, result.Error);
        Assert.Equal("New checkout", flag.Name);
        Assert.Equal("Old description.", flag.Description);
    }

    [Fact]
    public void UpdateDetails_WithOverlongName_ShouldFail()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        var result = flag.UpdateDetails(new string('a', FeatureFlag.MaxNameLength + 1), null, Now.AddHours(1), CausedBy);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.NameTooLong, result.Error);
    }

    [Fact]
    public void UpdateDetails_WithOverlongDescription_ShouldFail()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        var result = flag.UpdateDetails("New checkout", new string('a', FeatureFlag.MaxDescriptionLength + 1), Now.AddHours(1), CausedBy);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.DescriptionTooLong, result.Error);
    }

    [Fact]
    public void UpdateDetails_WhenNameAndDescriptionAreUnchanged_ShouldRaiseNoEventAndLeaveVersionAndUpdatedAtUnchanged()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", "Same description.", Nowhere, Now, CausedBy).Value;
        var eventCountAfterCreate = flag.UncommittedEvents.Count;
        var versionAfterCreate = flag.Version;

        var result = flag.UpdateDetails("New checkout", "Same description.", Now.AddHours(1), CausedBy);

        Assert.True(result.IsSuccess);
        Assert.Equal(eventCountAfterCreate, flag.UncommittedEvents.Count);
        Assert.Equal(versionAfterCreate, flag.Version);
        Assert.Equal(Now, flag.UpdatedAt);
    }

    [Fact]
    public void UpdateDetails_WhenChanged_ShouldRaiseExactlyOneFlagDetailsChangedEventAndIncrementVersion()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", "Old description.", Nowhere, Now, CausedBy).Value;
        var eventCountAfterCreate = flag.UncommittedEvents.Count;
        var versionAfterCreate = flag.Version;
        var later = Now.AddHours(1);

        flag.UpdateDetails("Renamed checkout", "New description.", later, CausedBy);

        Assert.Equal(eventCountAfterCreate + 1, flag.UncommittedEvents.Count);
        var detailsChanged = Assert.IsType<FlagDetailsChangedEvent>(flag.UncommittedEvents[^1]);
        Assert.Equal(flag.Id, detailsChanged.FlagId);
        Assert.Equal("Renamed checkout", detailsChanged.Name);
        Assert.Equal("New description.", detailsChanged.Description);
        Assert.Equal(later, detailsChanged.OccurredAt);
        Assert.Equal(CausedBy, detailsChanged.CausedBy);
        Assert.Equal(versionAfterCreate + 1, flag.Version);
    }

    [Fact]
    public void UpdateDetails_ShouldNotTouchStates()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, [EnvironmentKey.Development], Now, CausedBy).Value;

        flag.UpdateDetails("Renamed checkout", "New description.", Now.AddHours(1), CausedBy);

        Assert.True(flag.IsEnabledIn(EnvironmentKey.Development));
        Assert.Equal(Now, flag.StateIn(EnvironmentKey.Development).Match(state => state.UpdatedAt, () => default));
    }

    [Fact]
    public void Rehydrate_ShouldFoldEventsIntoTheSameStateAsTheOriginalInstance()
    {
        var original = FeatureFlag.Create(
            "new-checkout",
            "New checkout",
            "Rolls out the rewritten checkout.",
            [EnvironmentKey.Development],
            Now,
            CausedBy).Value;
        original.SetEnabled(EnvironmentKey.Staging, isEnabled: true, Now.AddHours(1), CausedBy);
        original.SetEnabled(EnvironmentKey.Development, isEnabled: false, Now.AddHours(2), CausedBy);

        var rehydrated = FeatureFlag.Rehydrate(original.Id, original.UncommittedEvents);

        Assert.Equal(original.Id, rehydrated.Id);
        Assert.Equal(original.Key, rehydrated.Key);
        Assert.Equal(original.Name, rehydrated.Name);
        Assert.Equal(original.Description, rehydrated.Description);
        Assert.Equal(original.CreatedAt, rehydrated.CreatedAt);
        Assert.Equal(original.UpdatedAt, rehydrated.UpdatedAt);
        Assert.Equal(original.Version, rehydrated.Version);
        Assert.All(EnvironmentKey.All, environment =>
            Assert.Equal(original.IsEnabledIn(environment), rehydrated.IsEnabledIn(environment)));
    }

    [Fact]
    public void Rehydrate_ShouldLeaveNoUncommittedEvents()
    {
        var original = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        var rehydrated = FeatureFlag.Rehydrate(original.Id, original.UncommittedEvents);

        Assert.Empty(rehydrated.UncommittedEvents);
    }

    [Fact]
    public void Rehydrate_WithAnEventForAnotherFlag_ShouldThrow()
    {
        var original = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        Assert.Throws<InvalidOperationException>(() => FeatureFlag.Rehydrate(Guid.CreateVersion7(), original.UncommittedEvents));
    }

    [Fact]
    public void Rehydrate_WithNoFlagCreatedEvent_ShouldThrow()
    {
        var original = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;
        var stateEventsOnly = original.UncommittedEvents.Skip(1);

        Assert.Throws<InvalidOperationException>(() => FeatureFlag.Rehydrate(original.Id, stateEventsOnly));
    }

    [Fact]
    public void UncommittedEvents_ShouldNotBeMutableThroughACastBackToAList()
    {
        var flag = FeatureFlag.Create("new-checkout", "New checkout", null, Nowhere, Now, CausedBy).Value;

        Assert.IsNotType<List<IFlagEvent>>(flag.UncommittedEvents);
    }
}
