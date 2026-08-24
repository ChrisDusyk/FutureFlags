using System.Security.Claims;
using FeatureFlags.Domain.SdkKeys;
using FeatureFlags.Domain.Shared;
using FeatureFlags.Server.Api;
using Microsoft.AspNetCore.Http;

namespace FeatureFlags.Server.Tests.Api;

/// <summary>
/// The rule that keeps segment definitions off a publishable key.
///
/// <para>
/// Asserted here rather than expressed as an authorization policy, and that is the behaviour worth
/// pinning: a policy failure answers a bare 403 with no body, which a client reports as "this key
/// may have been revoked" to somebody whose key is perfectly good and simply belongs on the other
/// route.
/// </para>
/// </summary>
public class SecretCredentialRuleTests
{
    private static HttpContext Request(SdkKeyKind? kind)
    {
        var context = new DefaultHttpContext();

        if (kind is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(AuthClaims.SdkKeyKind, kind.Value),
                    new Claim(AuthClaims.Environment, "dev"),
                ],
                AuthSchemes.SdkKey));
        }

        return context;
    }

    [Fact]
    public void APublishableKey_ShouldBeRefused()
    {
        var result = SecretCredentialRule.RequireSecret(Request(SdkKeyKind.Publishable));

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.PublishableKeyForRuleset, result.Error);
    }

    [Fact]
    public void TheRefusal_ShouldBeAForbiddenRatherThanAnUnauthorized()
    {
        // 401 means "prove who you are", and this caller already has. Answering it would send a
        // developer hunting for a revoked key instead of reading the sentence that says which
        // route to use.
        var result = SecretCredentialRule.RequireSecret(Request(SdkKeyKind.Publishable));

        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    [Fact]
    public void TheRefusal_ShouldSayWhereToGoInstead()
    {
        var result = SecretCredentialRule.RequireSecret(Request(SdkKeyKind.Publishable));

        Assert.Contains("POST /api/evaluation", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecretKey_ShouldBeAllowed()
    {
        Assert.True(SecretCredentialRule.RequireSecret(Request(SdkKeyKind.Secret)).IsSuccess);
    }

    [Fact]
    public void ARequestWithNoSdkKeyAtAll_ShouldPassThisRule()
    {
        // Not this rule's job. Authorization has already refused an unauthenticated request before
        // the endpoint runs, and a second opinion here would just be a second thing to keep in step.
        Assert.True(SecretCredentialRule.RequireSecret(Request(null)).IsSuccess);
    }
}
