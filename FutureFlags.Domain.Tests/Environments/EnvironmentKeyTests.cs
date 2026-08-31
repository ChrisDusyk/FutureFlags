using FutureFlags.Domain.Environments;

namespace FutureFlags.Domain.Tests.Environments;

public class EnvironmentKeyTests
{
    [Theory]
    [InlineData("dev")]
    [InlineData("stg")]
    [InlineData("prod")]
    public void Create_WithARecognizedKey_ShouldSucceed(string value)
    {
        var result = EnvironmentKey.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value.Value);
    }

    [Theory]
    [InlineData("DEV")]
    [InlineData("  prod  ")]
    [InlineData("Stg")]
    public void Create_ShouldNormalizeCaseAndWhitespace(string value)
    {
        var result = EnvironmentKey.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value.Trim().ToLowerInvariant(), result.Value.Value);
    }

    [Fact]
    public void Create_ShouldReturnTheSharedInstance()
    {
        // Nothing depends on this for correctness — EnvironmentKey is a record, so the == that
        // FeatureFlag.StateIn uses compares Value. What it pins down is that Create hands back the
        // instance from All rather than minting a new one, which is what keeps the closed set
        // genuinely closed at three objects instead of one per call.
        var result = EnvironmentKey.Create("prod");

        Assert.Same(EnvironmentKey.Production, result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNothing_ShouldFail(string? value)
    {
        var result = EnvironmentKey.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(EnvironmentErrors.KeyRequired, result.Error);
    }

    [Theory]
    [InlineData("qa")]
    [InlineData("production")]
    [InlineData("development")]
    public void Create_WithAnUnrecognizedKey_ShouldFail(string value)
    {
        // "production" and "development" are the console's display ids, not its keys — a caller
        // sending one has confused the two, and should be told so rather than silently ignored.
        var result = EnvironmentKey.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(EnvironmentErrors.KeyUnrecognized(value), result.Error);
    }

    [Fact]
    public void All_ShouldRunFromSafestToRealest()
    {
        Assert.Equal(
            [EnvironmentKey.Development, EnvironmentKey.Staging, EnvironmentKey.Production],
            EnvironmentKey.All);
    }

    [Fact]
    public void FromPersisted_WithAStoredValue_ShouldReturnTheSharedInstance()
    {
        Assert.Same(EnvironmentKey.Staging, EnvironmentKey.FromPersisted("stg"));
    }

    [Fact]
    public void FromPersisted_WithAnUnknownValue_ShouldThrow()
    {
        // Storage holding a value the application does not know is a broken invariant, not a
        // caller's mistake — throwing is right where a Result would be wrong.
        Assert.Throws<InvalidOperationException>(() => EnvironmentKey.FromPersisted("qa"));
    }

    [Fact]
    public void Ordinal_ShouldMatchPositionInAll()
    {
        // What FlagViewRepository.ListTargetingAsync and its fake sort by, in place of an
        // All.ToList().IndexOf(...) that allocated a fresh list per row being ordered.
        Assert.Equal(0, EnvironmentKey.Development.Ordinal);
        Assert.Equal(1, EnvironmentKey.Staging.Ordinal);
        Assert.Equal(2, EnvironmentKey.Production.Ordinal);
    }

    [Fact]
    public void Values_ShouldMatchWhatTheConsoleSends()
    {
        // These strings are the contract with frontend/src/shell/environment.ts. If this test is
        // failing because the set changed, the console's environments array has to change with it.
        Assert.Equal(["dev", "stg", "prod"], EnvironmentKey.All.Select(key => key.Value));
    }
}
