using System.Security.Claims;
using FutureFlags.Domain.Users;
using FutureFlags.Server.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FutureFlags.Server.Tests.Api;

/// <summary>
/// The policies as <c>AddConsoleAuthentication</c> actually registers them.
///
/// <para>
/// The scheme each policy accepts is the load-bearing part. <c>RequireAuthenticatedUser()</c> is
/// satisfied by <em>any</em> authenticated principal, so if the console's policies stopped naming
/// the JWT scheme an SDK key would pass <see cref="AuthPolicies.SignedIn"/> and be handed the whole
/// admin API. Nothing about the SDK key's claims prevents that on its own — the pinning does.
/// </para>
/// <para>
/// It is asserted on the policy rather than through <c>IAuthorizationService</c> because that is
/// where it lives: the authorization middleware re-authenticates using
/// <see cref="AuthorizationPolicy.AuthenticationSchemes"/> and builds the principal it evaluates
/// from those schemes alone. A policy naming no scheme accepts whatever the default produced.
/// </para>
/// </summary>
public class AuthPoliciesTests
{
    private static async Task<AuthorizationPolicy> GetPolicyAsync(string name)
    {
        var builder = Host.CreateApplicationBuilder();

        // The JWT handler resolves the auth service's address at registration time.
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["services:auth:http:0"] = "http://auth" });

        builder.AddConsoleAuthentication();

        var provider = builder.Build().Services.GetRequiredService<IAuthorizationPolicyProvider>();

        return await provider.GetPolicyAsync(name)
            ?? throw new InvalidOperationException($"No '{name}' policy is registered.");
    }

    [Theory]
    [InlineData(AuthPolicies.SignedIn, AuthSchemes.Jwt)]
    [InlineData(AuthPolicies.Admin, AuthSchemes.Jwt)]
    [InlineData(AuthPolicies.SdkKey, AuthSchemes.SdkKey)]
    public async Task EveryPolicy_ShouldAcceptExactlyOneScheme(string policyName, string expectedScheme)
    {
        var policy = await GetPolicyAsync(policyName);

        Assert.Equal([expectedScheme], policy.AuthenticationSchemes);
    }

    [Fact]
    public async Task TheConsolePolicies_ShouldNotAcceptAnSdkKey()
    {
        foreach (var name in new[] { AuthPolicies.SignedIn, AuthPolicies.Admin })
        {
            Assert.DoesNotContain(AuthSchemes.SdkKey, (await GetPolicyAsync(name)).AuthenticationSchemes);
        }
    }

    [Fact]
    public async Task TheSdkKeyPolicy_ShouldNotAcceptAUsersToken()
    {
        Assert.DoesNotContain(AuthSchemes.Jwt, (await GetPolicyAsync(AuthPolicies.SdkKey)).AuthenticationSchemes);
    }

    /// <summary>
    /// The second line of defence, and the one a principal carries on its own: an SDK key has no
    /// user behind it and so no role, which is what the admin policy asks for.
    /// </summary>
    [Fact]
    public async Task Admin_ShouldRejectAPrincipalWithoutTheAdminRole()
    {
        var policy = await GetPolicyAsync(AuthPolicies.Admin);

        var sdkKey = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AuthClaims.Environment, "dev")],
            AuthSchemes.SdkKey,
            AuthClaims.SdkKeyName,
            AuthClaims.Role));

        var ordinaryUser = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AuthClaims.Role, UserRole.User.Value)],
            AuthSchemes.Jwt,
            AuthClaims.Email,
            AuthClaims.Role));

        Assert.False(await SatisfiesAsync(policy, sdkKey));
        Assert.False(await SatisfiesAsync(policy, ordinaryUser));
    }

    [Fact]
    public async Task Admin_ShouldAcceptAnAdmin()
    {
        var policy = await GetPolicyAsync(AuthPolicies.Admin);

        var admin = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AuthClaims.Role, UserRole.Admin.Value)],
            AuthSchemes.Jwt,
            AuthClaims.Email,
            AuthClaims.Role));

        Assert.True(await SatisfiesAsync(policy, admin));
    }

    private static async Task<bool> SatisfiesAsync(AuthorizationPolicy policy, ClaimsPrincipal principal)
    {
        var services = new ServiceCollection()
            .AddAuthorization()
            .AddLogging()
            .BuildServiceProvider();

        var result = await services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, resource: null, policy);

        return result.Succeeded;
    }
}
