using FutureFlags.Domain.Flags;

namespace FutureFlags.Domain.Tests.Flags;

public class FlagValueTypeTests
{
    [Fact]
    public void Create_WithNothing_ShouldBeBoolean()
    {
        // Every caller in this build creates a boolean flag without saying so.
        var result = FlagValueType.Create(null);

        Assert.True(result.IsSuccess);
        Assert.Equal(FlagValueType.Boolean, result.Value);
    }

    [Theory]
    [InlineData("boolean")]
    [InlineData("BOOLEAN")]
    [InlineData("  Boolean  ")]
    public void Create_WithBoolean_ShouldSucceed(string value)
    {
        Assert.Equal(FlagValueType.Boolean, FlagValueType.Create(value).Value);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("number")]
    [InlineData("object")]
    public void Create_WithATypeThisBuildCannotAuthor_ShouldSayItIsUnsupported(string value)
    {
        var result = FlagValueType.Create(value);

        Assert.True(result.IsFailure);

        // Not "unrecognized": the name is real, the feature has not shipped. A caller can only act
        // on the difference if we keep the two errors apart.
        Assert.Equal("Flag.ValueType.NotSupported", result.Error.Code);
    }

    [Fact]
    public void Create_WithANameThatIsNotAType_ShouldSayItIsUnrecognized()
    {
        var result = FlagValueType.Create("bool");

        Assert.True(result.IsFailure);
        Assert.Equal("Flag.ValueType.Unrecognized", result.Error.Code);
    }

    [Theory]
    [InlineData("boolean")]
    [InlineData("string")]
    [InlineData("number")]
    [InlineData("object")]
    public void FromPersisted_ShouldAcceptEveryType(string value)
    {
        // Unlike Create, which refuses what this build cannot author. A stream written by a later
        // build has to replay here rather than throwing halfway through a history.
        Assert.Equal(value, FlagValueType.FromPersisted(value).Value);
    }

    [Fact]
    public void FromPersisted_WithNothing_ShouldBeBoolean() =>
        Assert.Equal(FlagValueType.Boolean, FlagValueType.FromPersisted(null));

    [Fact]
    public void FromPersisted_WithSomethingUnknown_ShouldThrow() =>
        Assert.Throws<InvalidOperationException>(() => FlagValueType.FromPersisted("bool"));

    [Fact]
    public void OnlyBoolean_ShouldBeAuthorable()
    {
        // The guard on the migration: turning another type on is a deliberate act, not a default
        // that drifts in.
        Assert.Equal([FlagValueType.Boolean], FlagValueType.All.Where(type => type.IsAuthorable));
    }
}
