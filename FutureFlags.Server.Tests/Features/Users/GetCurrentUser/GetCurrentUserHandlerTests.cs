using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;
using FutureFlags.Server.Features.Users.GetCurrentUser;
using FutureFlags.Server.Tests.Fakes;

namespace FutureFlags.Server.Tests.Features.Users.GetCurrentUser;

public class GetCurrentUserHandlerTests
{
    private static readonly DateTimeOffset Created = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeUserRepository _repository = new();

    private GetCurrentUserHandler CreateSut() => new(_repository);

    private User Seed(UserRole role)
    {
        var user = User.FromPersisted(
            Guid.CreateVersion7(),
            "ada@example.com",
            "Ada Lovelace",
            role,
            Created,
            Created);

        _repository.Seed(user);

        return user;
    }

    [Fact]
    public async Task HandleAsync_WithMirroredUser_ShouldReturnTheirDetails()
    {
        var user = Seed(UserRole.User);

        var result = await CreateSut().HandleAsync(user.Id, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        var response = result.Value;
        Assert.Equal(user.Id, response.Id);
        Assert.Equal("ada@example.com", response.Email);
        Assert.Equal("Ada Lovelace", response.Name);
        Assert.Equal("user", response.Role);
        Assert.False(response.IsAdmin);
    }

    [Fact]
    public async Task HandleAsync_WithAdmin_ShouldReportTheElevatedRole()
    {
        var user = Seed(UserRole.Admin);

        var result = await CreateSut().HandleAsync(user.Id, TestContext.Current.CancellationToken);

        Assert.Equal("admin", result.Value.Role);
        Assert.True(result.Value.IsAdmin);
    }

    [Fact]
    public async Task HandleAsync_WhenNoRowHasBeenMirrored_ShouldFailAsUnauthorized()
    {
        // A valid token for an identity Better Auth knows about but the trigger has not copied
        // across. Treating it as unauthorized rather than not-found is deliberate: from the
        // caller's side the account is not usable yet, and that is an authentication problem.
        var result = await CreateSut().HandleAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal("User.NotProvisioned", result.Error.Code);
    }
}
