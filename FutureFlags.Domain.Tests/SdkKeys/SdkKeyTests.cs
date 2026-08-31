using FutureFlags.Domain.Environments;
using FutureFlags.Domain.SdkKeys;

namespace FutureFlags.Domain.Tests.SdkKeys;

public class SdkKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.CreateVersion7();

    private static IssuedSdkKey Issue(
        string name = "CI",
        EnvironmentKey? environment = null,
        SdkKeyKind? kind = null) =>
        SdkKey.Issue(name, kind ?? SdkKeyKind.Secret, environment ?? EnvironmentKey.Development, Admin, Now).Value;

    [Fact]
    public void Issue_ShouldReturnAKeyAndItsToken()
    {
        var issued = Issue("web app", EnvironmentKey.Production);

        Assert.Equal("web app", issued.Key.Name);
        Assert.Equal(EnvironmentKey.Production, issued.Key.Environment);
        Assert.Equal(Admin, issued.Key.CreatedBy);
        Assert.Equal(Now, issued.Key.CreatedAt);
        Assert.True(issued.Key.IsActive);
        Assert.StartsWith($"{SdkKeyKind.Secret.TokenPrefix}_prod_", issued.Token);
    }

    [Fact]
    public void Issue_ShouldTrimTheName()
    {
        Assert.Equal("CI", Issue("  CI  ").Key.Name);
    }

    [Fact]
    public void Issue_ShouldStartUnused()
    {
        Assert.True(Issue().Key.LastUsedAt.IsNone);
        Assert.True(Issue().Key.RevokedAt.IsNone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Issue_WithoutAName_ShouldFail(string? name)
    {
        var result = SdkKey.Issue(name, SdkKeyKind.Secret, EnvironmentKey.Development, Admin, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.NameRequired.Code, result.Error.Code);
    }

    [Fact]
    public void Issue_WithAnOverlongName_ShouldFail()
    {
        var result = SdkKey.Issue(new string('a', SdkKey.MaxNameLength + 1), SdkKeyKind.Secret, EnvironmentKey.Development, Admin, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.NameTooLong.Code, result.Error.Code);
    }

    [Fact]
    public void Matches_WithTheIssuedToken_ShouldSucceed()
    {
        var issued = Issue();

        var credential = SdkKeyToken.Parse(issued.Token).Value;

        Assert.True(issued.Key.Matches(credential));
    }

    [Fact]
    public void Matches_WithAnotherKeysToken_ShouldFail()
    {
        var issued = Issue();
        var other = Issue();

        var credential = SdkKeyToken.Parse(other.Token).Value;

        Assert.False(issued.Key.Matches(credential));
    }

    [Fact]
    public void Revoke_ShouldRetireTheKey()
    {
        var issued = Issue();
        var revokedAt = Now.AddDays(3);

        var result = issued.Key.Revoke(revokedAt);

        Assert.True(result.IsSuccess);
        Assert.False(issued.Key.IsActive);
        Assert.Equal(revokedAt, issued.Key.RevokedAt.Reduce(default(DateTimeOffset)));
    }

    [Fact]
    public void Revoke_Twice_ShouldConflict()
    {
        var issued = Issue();
        issued.Key.Revoke(Now);

        var result = issued.Key.Revoke(Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.AlreadyRevoked.Code, result.Error.Code);
    }

    /// <summary>
    /// A revoked key keeps its secret. Verification and revocation are separate questions, and the
    /// authentication handler asks both — collapsing them here would make a revoked key
    /// indistinguishable from a forged one to anything that reads this type.
    /// </summary>
    [Fact]
    public void Matches_ShouldStillSucceedForARevokedKey()
    {
        var issued = Issue();
        var credential = SdkKeyToken.Parse(issued.Token).Value;

        issued.Key.Revoke(Now);

        Assert.True(issued.Key.Matches(credential));
        Assert.False(issued.Key.IsActive);
    }

    [Fact]
    public void MarkUsed_TheFirstTime_ShouldRecordIt()
    {
        var issued = Issue();

        Assert.True(issued.Key.MarkUsed(Now));
        Assert.Equal(Now, issued.Key.LastUsedAt.Reduce(default(DateTimeOffset)));
    }

    [Fact]
    public void MarkUsed_WithinTheResolution_ShouldNotWriteAgain()
    {
        var issued = Issue();
        issued.Key.MarkUsed(Now);

        var soon = Now + SdkKey.LastUsedResolution - TimeSpan.FromMinutes(1);

        // False is what keeps a read-only endpoint from writing on every single request.
        Assert.False(issued.Key.MarkUsed(soon));
        Assert.Equal(Now, issued.Key.LastUsedAt.Reduce(default(DateTimeOffset)));
    }

    [Fact]
    public void MarkUsed_PastTheResolution_ShouldRecordItAgain()
    {
        var issued = Issue();
        issued.Key.MarkUsed(Now);

        var later = Now + SdkKey.LastUsedResolution;

        Assert.True(issued.Key.MarkUsed(later));
        Assert.Equal(later, issued.Key.LastUsedAt.Reduce(default(DateTimeOffset)));
    }
}
