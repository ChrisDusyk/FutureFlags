using System.Security.Claims;
using FutureFlags.Domain.Shared;
using FutureFlags.Domain.Users;
using FutureFlags.Server.Api;

namespace FutureFlags.Server.Tests.Api;

public class AuthClaimsTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Bearer", AuthClaims.Email, AuthClaims.Role));

    [Fact]
    public void GetUserId_WithSubjectClaim_ShouldReturnTheId()
    {
        var id = Guid.CreateVersion7();

        var principal = PrincipalWith(new Claim(AuthClaims.Subject, id.ToString()));

        Assert.Equal(Option<Guid>.Some(id), principal.GetUserId());
    }

    [Fact]
    public void GetUserId_WithNoSubjectClaim_ShouldReturnNone()
    {
        var principal = PrincipalWith(new Claim(AuthClaims.Email, "ada@example.com"));

        Assert.True(principal.GetUserId().IsNone);
    }

    [Fact]
    public void GetUserId_WithASubjectThatIsNotAGuid_ShouldReturnNone()
    {
        // The auth service mints UUIDv7 ids, but the claim arrives from outside and so is parsed
        // rather than assumed.
        var principal = PrincipalWith(new Claim(AuthClaims.Subject, "not-a-guid"));

        Assert.True(principal.GetUserId().IsNone);
    }

    [Fact]
    public void RoleClaim_ShouldSatisfyTheAdminPolicysRequirement()
    {
        // The claim name the auth service writes has to be the one RequireRole reads, or the
        // admin policy silently never matches. This is what ties the two together.
        var principal = PrincipalWith(
            new Claim(AuthClaims.Subject, Guid.CreateVersion7().ToString()),
            new Claim(AuthClaims.Role, UserRole.Admin.Value));

        Assert.True(principal.IsInRole(UserRole.Admin.Value));
        Assert.False(principal.IsInRole(UserRole.User.Value));
    }
}
