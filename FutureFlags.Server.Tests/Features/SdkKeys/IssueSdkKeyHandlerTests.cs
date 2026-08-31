using FutureFlags.Domain.Environments;
using FutureFlags.Domain.SdkKeys;
using FutureFlags.Server.Features.SdkKeys.IssueSdkKey;
using FutureFlags.Server.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace FutureFlags.Server.Tests.Features.SdkKeys;

public class IssueSdkKeyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.CreateVersion7();

    private readonly FakeSdkKeyRepository _repository = new();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private IssueSdkKeyHandler CreateSut() => new(_repository, _timeProvider);

    [Fact]
    public async Task HandleAsync_ShouldPersistTheKeyAndReturnItsToken()
    {
        var result = await CreateSut().HandleAsync(
            new IssueSdkKeyCommand("CI", SdkKeyKind.Secret, EnvironmentKey.Development, Admin),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("CI", result.Value.Name);
        Assert.Equal("dev", result.Value.Environment);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.Equal(1, _repository.SaveChangesCallCount);

        var stored = Assert.Single(_repository.Committed);
        Assert.Equal(result.Value.Id, stored.Id);
        Assert.Equal(Admin, stored.CreatedBy);
    }

    /// <summary>
    /// The token the caller is handed has to be one that actually authenticates, and the stored row
    /// has to be able to recognise it. This is the seam where a hashing mistake would hide.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ShouldReturnATokenTheStoredKeyAccepts()
    {
        var result = await CreateSut().HandleAsync(
            new IssueSdkKeyCommand("CI", SdkKeyKind.Secret, EnvironmentKey.Production, Admin),
            TestContext.Current.CancellationToken);

        var credential = SdkKeyToken.Parse(result.Value.Token);

        Assert.True(credential.IsSuccess);

        var stored = Assert.Single(_repository.Committed);
        Assert.Equal(stored.Selector, credential.Value.Selector);
        Assert.True(stored.Matches(credential.Value));
    }

    [Fact]
    public async Task HandleAsync_WithoutAName_ShouldFailAndPersistNothing()
    {
        var result = await CreateSut().HandleAsync(
            new IssueSdkKeyCommand("  ", SdkKeyKind.Secret, EnvironmentKey.Development, Admin),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.NameRequired.Code, result.Error.Code);
        Assert.Empty(_repository.Committed);
        Assert.Equal(0, _repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_ShouldScopeTheKeyToTheEnvironmentAsked()
    {
        var result = await CreateSut().HandleAsync(
            new IssueSdkKeyCommand("staging runner", SdkKeyKind.Secret, EnvironmentKey.Staging, Admin),
            TestContext.Current.CancellationToken);

        Assert.Equal(EnvironmentKey.Staging, Assert.Single(_repository.Committed).Environment);
        Assert.Contains("_stg_", result.Value.Token);
    }
}
