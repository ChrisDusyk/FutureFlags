using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;

namespace FutureFlags.Domain.Tests.Users;

public class UserRoleTests
{
    [Theory]
    [InlineData("user")]
    [InlineData("admin")]
    public void Create_WithRecognizedRole_ShouldSucceed(string value)
    {
        var result = UserRole.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value.Value);
    }

    [Theory]
    [InlineData("ADMIN", "admin")]
    [InlineData("  Admin  ", "admin")]
    [InlineData("User", "user")]
    public void Create_ShouldNormalizeCasingAndWhitespace(string input, string expected)
    {
        var result = UserRole.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingValue_ShouldFailAsRequired(string? value)
    {
        var result = UserRole.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.RoleRequired, result.Error);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("superuser")]
    // Better Auth's admin plugin allows a comma-separated list. The application recognizes single
    // roles only, which is why the mirror trigger collapses a list before it reaches this type.
    [InlineData("user,admin")]
    public void Create_WithUnrecognizedRole_ShouldFailAsValidation(string value)
    {
        var result = UserRole.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("User.Role.Unrecognized", result.Error.Code);
    }

    [Fact]
    public void Create_ShouldReturnTheSharedInstance()
    {
        var result = UserRole.Create("admin");

        Assert.Same(UserRole.Admin, result.Value);
    }

    [Fact]
    public void IsAdmin_ShouldDistinguishTheTwoRoles()
    {
        Assert.True(UserRole.Admin.IsAdmin);
        Assert.False(UserRole.User.IsAdmin);
    }

    [Fact]
    public void FromPersisted_WithStoredValue_ShouldRoundTrip()
    {
        Assert.Same(UserRole.Admin, UserRole.FromPersisted("admin"));
        Assert.Same(UserRole.User, UserRole.FromPersisted("user"));
    }

    [Fact]
    public void FromPersisted_WithUnrecognizedValue_ShouldThrow()
    {
        // Unlike Create, this is not a validation path — a row holding something else means the
        // mirror trigger and this type have gone out of step, which is a defect, not a bad request.
        Assert.Throws<InvalidOperationException>(() => UserRole.FromPersisted("owner"));
    }

    [Fact]
    public void All_ShouldContainExactlyTheTwoRoles()
    {
        Assert.Equal([UserRole.User, UserRole.Admin], UserRole.All);
    }
}
