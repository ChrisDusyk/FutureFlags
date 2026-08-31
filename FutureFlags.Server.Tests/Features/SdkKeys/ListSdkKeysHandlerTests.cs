using FutureFlags.Domain.Environments;
using FutureFlags.Domain.SdkKeys;
using FutureFlags.Server.Features.SdkKeys.ListSdkKeys;
using FutureFlags.Server.Tests.Fakes;

namespace FutureFlags.Server.Tests.Features.SdkKeys;

public class ListSdkKeysHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.CreateVersion7();

    private readonly FakeSdkKeyRepository _repository = new();

    private ListSdkKeysHandler CreateSut() => new(_repository);

    private IssuedSdkKey Seed(string name, EnvironmentKey environment)
    {
        var issued = SdkKey.Issue(name, SdkKeyKind.Secret, environment, Admin, Now).Value;
        _repository.Seed(issued.Key);

        return issued;
    }

    [Fact]
    public async Task HandleAsync_WithNoKeys_ShouldReturnAnEmptyList()
    {
        var result = await CreateSut().HandleAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Keys);
    }

    [Fact]
    public async Task HandleAsync_ShouldSummariseEachKey()
    {
        var issued = Seed("CI", EnvironmentKey.Production);

        var result = await CreateSut().HandleAsync(TestContext.Current.CancellationToken);

        var summary = Assert.Single(result.Value.Keys);
        Assert.Equal(issued.Key.Id, summary.Id);
        Assert.Equal("CI", summary.Name);
        Assert.Equal("prod", summary.Environment);
        Assert.Equal(Now, summary.CreatedAt);
        Assert.Null(summary.LastUsedAt);
        Assert.Null(summary.RevokedAt);
        Assert.True(summary.IsActive);
    }

    /// <summary>
    /// The whole reason this response has its own type. A summary that carried enough to
    /// reconstruct a token would make the list endpoint a way to read every credential in the
    /// system, which is exactly what hashing them at rest is meant to prevent.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ShouldNotReturnAnythingThatAuthenticates()
    {
        var issued = Seed("CI", EnvironmentKey.Development);

        var result = await CreateSut().HandleAsync(TestContext.Current.CancellationToken);

        var summary = Assert.Single(result.Value.Keys);

        Assert.NotEqual(issued.Token, summary.Hint);
        Assert.True(SdkKeyToken.Parse(summary.Hint).IsFailure);
        Assert.DoesNotContain(issued.Token.Split('_')[3], summary.Hint);
    }

    [Fact]
    public async Task HandleAsync_ShouldShowTheHintTheConsoleMatchesAgainstAConfigFile()
    {
        var issued = Seed("CI", EnvironmentKey.Staging);

        var result = await CreateSut().HandleAsync(TestContext.Current.CancellationToken);

        var summary = Assert.Single(result.Value.Keys);

        Assert.Equal($"ffs_stg_{issued.Key.Selector}", summary.Hint);
        Assert.StartsWith(summary.Hint, issued.Token);
    }

    [Fact]
    public async Task HandleAsync_ShouldIncludeRevokedKeys()
    {
        var issued = Seed("retired", EnvironmentKey.Development);
        issued.Key.Revoke(Now.AddDays(1));

        var result = await CreateSut().HandleAsync(TestContext.Current.CancellationToken);

        var summary = Assert.Single(result.Value.Keys);
        Assert.False(summary.IsActive);
        Assert.Equal(Now.AddDays(1), summary.RevokedAt);
    }

    [Fact]
    public async Task HandleAsync_ShouldReportWhenAKeyWasLastUsed()
    {
        var issued = Seed("CI", EnvironmentKey.Development);
        issued.Key.MarkUsed(Now.AddHours(4));

        var result = await CreateSut().HandleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Now.AddHours(4), Assert.Single(result.Value.Keys).LastUsedAt);
    }
}
