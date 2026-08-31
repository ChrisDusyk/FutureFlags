using FutureFlags.Domain.Environments;
using FutureFlags.Domain.SdkKeys;

namespace FutureFlags.Domain.Tests.SdkKeys;

public class SdkKeyKindTests
{
    [Fact]
    public void TheTwoKinds_ShouldHaveDistinctPrefixes()
    {
        Assert.Equal("ffs", SdkKeyKind.Secret.TokenPrefix);
        Assert.Equal("ffp", SdkKeyKind.Publishable.TokenPrefix);

        // The prefix is how a person reading a configuration file tells the two apart at a glance.
        // If they ever collided, that glance would be wrong rather than merely unhelpful.
        Assert.Equal(
            SdkKeyKind.All.Count,
            SdkKeyKind.All.Select(kind => kind.TokenPrefix).Distinct().Count());
    }

    [Fact]
    public void OnlyPublishable_ShouldBePublishable()
    {
        Assert.True(SdkKeyKind.Publishable.IsPublishable);
        Assert.False(SdkKeyKind.Secret.IsPublishable);
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("publishable")]
    [InlineData("  SECRET  ")]
    public void Create_WithAKnownKind_ShouldSucceed(string value)
    {
        var result = SdkKeyKind.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value.Trim().ToLowerInvariant(), result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNoKind_ShouldFailAsRequired(string? value)
    {
        var result = SdkKeyKind.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.KindRequired.Code, result.Error.Code);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("ffs")]
    public void Create_WithSomethingElse_ShouldFailAsUnrecognized(string value)
    {
        var result = SdkKeyKind.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.KindUnrecognized(value).Code, result.Error.Code);
    }

    [Fact]
    public void FromTokenPrefix_ShouldRoundTrip()
    {
        foreach (var kind in SdkKeyKind.All)
        {
            Assert.Equal(kind, SdkKeyKind.FromTokenPrefix(kind.TokenPrefix).Reduce(() => null!));
        }

        Assert.True(SdkKeyKind.FromTokenPrefix("xyz").IsNone);
    }

    [Fact]
    public void FromPersisted_WithAnUnknownValue_ShouldThrow() =>
        Assert.Throws<InvalidOperationException>(() => SdkKeyKind.FromPersisted("public"));

    [Fact]
    public void Issue_ShouldStampTheKindsPrefixOnTheToken()
    {
        foreach (var kind in SdkKeyKind.All)
        {
            var token = SdkKeyToken.Issue(kind, EnvironmentKey.Production);

            Assert.StartsWith($"{kind.TokenPrefix}_prod_", token.Value);
        }
    }

    /// <summary>
    /// Both kinds have to survive the scheme selector, or a publishable key would be handed to the
    /// JWT handler and rejected as a malformed token rather than authenticated.
    /// </summary>
    [Fact]
    public void LooksLikeSdkKey_ShouldRecogniseBothKinds()
    {
        foreach (var kind in SdkKeyKind.All)
        {
            var token = SdkKeyToken.Issue(kind, EnvironmentKey.Development);

            Assert.True(SdkKeyToken.LooksLikeSdkKey(token.Value));
            Assert.True(SdkKeyToken.Parse(token.Value).IsSuccess);
        }
    }

    [Fact]
    public void LooksLikeSdkKey_ShouldStillRejectAJwt() =>
        Assert.False(SdkKeyToken.LooksLikeSdkKey("eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiIxIn0.signature"));

    /// <summary>
    /// A key's kind comes from its row, exactly as its environment does. A token claiming to be
    /// publishable is not one, and the verification path never consults the prefix.
    /// </summary>
    [Fact]
    public void APresentedPrefix_ShouldNotDecideWhatAKeyIs()
    {
        var issued = SdkKey.Issue("CI", SdkKeyKind.Secret, EnvironmentKey.Development, Guid.CreateVersion7(), DateTimeOffset.UtcNow).Value;

        var relabelled = string.Concat("ffp", issued.Token.AsSpan(3));

        var parsed = SdkKeyToken.Parse(relabelled);

        Assert.True(parsed.IsSuccess);
        Assert.True(issued.Key.Matches(parsed.Value));
        Assert.Equal(SdkKeyKind.Secret, issued.Key.Kind);
    }
}
