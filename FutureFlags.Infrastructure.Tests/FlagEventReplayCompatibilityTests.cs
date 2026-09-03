using System.Text.Json;
using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Flags.Events;
using FutureFlags.Evaluation;
using FutureFlags.Infrastructure.Persistence.Events;
using FutureFlags.Infrastructure.Persistence.ReadModels;

namespace FutureFlags.Infrastructure.Tests;

/// <summary>
/// That an event stream written before variants existed still replays, and that one written after
/// them is still readable by a build that predates them.
///
/// <para>
/// This is the test the whole shape of the variants work rests on. <see cref="FeatureFlag"/>'s
/// <c>Apply</c> and <see cref="FlagEventSerializer"/>'s <c>ToEvent</c> both throw on an event
/// <em>type</em> they do not recognize, which is what made shipping <c>FlagTargetingChanged</c> a
/// one-way deploy. Variants avoided that by adding fields to two existing types instead — but that
/// only holds if the payload really does tolerate the fields being absent in one direction and
/// unexpected in the other. Nothing else in the suite asks.
/// </para>
/// <para>
/// No database: this is the serializer and the aggregate, and both are pure. It sits in this
/// project because <see cref="FlagEventSerializer"/> is internal to Infrastructure.
/// </para>
/// </summary>
public class FlagEventReplayCompatibilityTests
{
    private static readonly Guid FlagId = Guid.CreateVersion7();
    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void APreVariantsStream_ShouldReplayAsABooleanFlag()
    {
        // Exactly the payloads a build before this migration wrote — no valueType, no variants, no
        // onVariant, no offVariant. This is also the shape AddFlagEvents' backfill SQL writes.
        var events = new[]
        {
            Record(FlagEventSerializer.FlagCreatedEventType, """
                {"Key":"legacy-flag","Name":"Legacy flag","Description":"Written before variants"}
                """, 1),
            Record(FlagEventSerializer.FlagStateChangedEventType, """
                {"Environment":"prod","IsEnabled":true}
                """, 2),
        };

        var flag = FeatureFlag.Rehydrate(FlagId, events.Select(FlagEventSerializer.ToEvent));

        Assert.Equal(FlagValueType.Boolean, flag.ValueType);
        Assert.Equal(FlagVariants.BooleanPair, flag.Variants);

        var state = flag.StateIn(Domain.Environments.EnvironmentKey.Production);

        Assert.True(state.Match(candidate => candidate.IsEnabled, () => false));
        Assert.Equal(FlagVariantNames.On, state.Match(candidate => candidate.OnVariant, () => null!));
        Assert.Equal(FlagVariantNames.Off, state.Match(candidate => candidate.OffVariant, () => null!));
    }

    [Fact]
    public void APostVariantsStream_ShouldBeReadableByAReaderThatIgnoresTheNewFields()
    {
        // The other direction, and the one that decides whether this deploy can be rolled back. A
        // build that predates variants deserializes into a payload record without those members;
        // PayloadOptions leaves UnmappedMemberHandling at its default, which skips them. Modelled
        // here by deserializing today's payload into the old shape.
        var created = new FlagCreatedEvent(
            FlagId,
            FlagKey.FromPersisted("modern-flag"),
            "Modern flag",
            "Written with variants",
            FlagValueType.Boolean,
            FlagVariants.BooleanPair,
            When,
            CausedBy: null);

        var record = FlagEventSerializer.ToRecord(FlagId, 1, created);
        var asOldReader = JsonSerializer.Deserialize<PreVariantsCreatedPayload>(
            record.Payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(asOldReader);
        Assert.Equal("modern-flag", asOldReader.Key);
        Assert.Equal("Modern flag", asOldReader.Name);
    }

    [Fact]
    public void AVariantCarryingStream_ShouldReplayTheVariantsItNames()
    {
        // Not a flag this build can author — the point is that the payload carries the fields
        // faithfully, so the day a typed flag ships its history is already readable.
        var events = new[]
        {
            Record(FlagEventSerializer.FlagCreatedEventType, """
                {"Key":"renamed","Name":"Renamed","Description":"",
                 "ValueType":"boolean","Variants":{"enabled":true,"disabled":false}}
                """, 1),
            Record(FlagEventSerializer.FlagStateChangedEventType, """
                {"Environment":"dev","IsEnabled":true,"OnVariant":"enabled","OffVariant":"disabled"}
                """, 2),
        };

        var flag = FeatureFlag.Rehydrate(FlagId, events.Select(FlagEventSerializer.ToEvent));
        var state = flag.StateIn(Domain.Environments.EnvironmentKey.Development);

        Assert.Equal("enabled", state.Match(candidate => candidate.OnVariant, () => null!));
        Assert.Equal("disabled", state.Match(candidate => candidate.OffVariant, () => null!));
        Assert.True(flag.Variants.Contains("enabled"));
        Assert.True(flag.Variants.Contains("disabled"));
    }

    [Fact]
    public void ABooleanFlagsVariants_ShouldRoundTripThroughThePayload()
    {
        // The migration's backfill writes this JSON by hand, so what the serializer produces and
        // what the column default says have to mean the same thing.
        var created = new FlagCreatedEvent(
            FlagId, FlagKey.FromPersisted("f"), "F", "", When, causedBy: null);

        var payload = FlagEventSerializer.ToRecord(FlagId, 1, created).Payload;

        Assert.Contains("\"on\":true", payload, StringComparison.Ordinal);
        Assert.Contains("\"off\":false", payload, StringComparison.Ordinal);
        Assert.Contains("\"boolean\"", payload, StringComparison.Ordinal);
    }

    private static FlagEventRecord Record(string eventType, string payload, int sequenceNumber) => new()
    {
        FlagId = FlagId,
        SequenceNumber = sequenceNumber,
        EventType = eventType,
        Payload = payload,
        OccurredAt = When,
        CausedBy = null,
    };

    /// <summary>The <c>FlagCreated</c> payload as a build predating variants declares it.</summary>
    private sealed record PreVariantsCreatedPayload(string Key, string Name, string Description);
}
