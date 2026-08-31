using FutureFlags.Domain.Users;

namespace FutureFlags.Domain.Tests.Users;

public class UserTests
{
    private static readonly DateTimeOffset Created = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    private static User Build(UserRole role) => User.FromPersisted(
        Guid.CreateVersion7(),
        "ada@example.com",
        "Ada Lovelace",
        role,
        Created,
        Created.AddDays(1));

    [Fact]
    public void FromPersisted_ShouldKeepEveryFieldAsGiven()
    {
        var id = Guid.CreateVersion7();

        var user = User.FromPersisted(id, "ada@example.com", "Ada Lovelace", UserRole.User, Created, Created);

        Assert.Equal(id, user.Id);
        Assert.Equal("ada@example.com", user.Email);
        Assert.Equal("Ada Lovelace", user.Name);
        Assert.Same(UserRole.User, user.Role);
        Assert.Equal(Created, user.CreatedAt);
        Assert.Equal(Created, user.UpdatedAt);
    }

    [Fact]
    public void IsAdmin_ShouldFollowTheRole()
    {
        Assert.True(Build(UserRole.Admin).IsAdmin);
        Assert.False(Build(UserRole.User).IsAdmin);
    }
}
