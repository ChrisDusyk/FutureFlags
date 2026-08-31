using System.Security.Claims;
using FutureFlags.Domain.SdkKeys;
using FutureFlags.Server.Api;
using Microsoft.AspNetCore.Http;

namespace FutureFlags.Server.Tests.Api;

/// <summary>
/// The rule that keeps a secret key out of a browser. It is asserted here, on this side, rather
/// than through CORS — a published credential can be lifted out of a bundle and replayed from
/// anywhere, and no amount of CORS configuration has an opinion about that.
/// </summary>
public class BrowserCredentialRuleTests
{
    private static HttpContext Request(SdkKeyKind? kind, string? origin)
    {
        var context = new DefaultHttpContext();

        if (kind is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(AuthClaims.SdkKeyKind, kind.Value),
                    new Claim(AuthClaims.Environment, "dev")
                ],
                AuthSchemes.SdkKey));
        }

        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        return context;
    }

    [Fact]
    public void ASecretKeyFromABrowser_ShouldBeRefused()
    {
        var result = BrowserCredentialRule.Check(Request(SdkKeyKind.Secret, "https://app.example.com"));

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.SecretKeyFromBrowser.Code, result.Error.Code);
    }

    [Fact]
    public void APublishableKeyFromABrowser_ShouldBeAllowed() =>
        Assert.True(BrowserCredentialRule.Check(Request(SdkKeyKind.Publishable, "https://app.example.com")).IsSuccess);

    [Fact]
    public void ASecretKeyFromAServer_ShouldBeAllowed() =>
        // No Origin header, so nothing about this request says it came from a browser. This is the
        // ordinary case and the one that must not regress.
        Assert.True(BrowserCredentialRule.Check(Request(SdkKeyKind.Secret, origin: null)).IsSuccess);

    [Fact]
    public void APublishableKeyFromAServer_ShouldBeAllowed() =>
        Assert.True(BrowserCredentialRule.Check(Request(SdkKeyKind.Publishable, origin: null)).IsSuccess);

    /// <summary>
    /// The failure has to be closed rather than open: a principal carrying no kind at all is not
    /// treated as publishable just because nothing said otherwise.
    /// </summary>
    [Fact]
    public void ARequestWithNoKindClaim_ShouldBeRefusedFromABrowser()
    {
        var result = BrowserCredentialRule.Check(Request(kind: null, "https://app.example.com"));

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.SecretKeyFromBrowser.Code, result.Error.Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("https://app.example.com")]
    [InlineData("http://localhost:5173")]
    public void AnyOrigin_ShouldCountAsABrowser(string origin) =>
        // Including the literal "null" a sandboxed or file:// document sends. Which origins may
        // *read* the answer is CORS's business; whether a secret key may be used at all is not.
        Assert.True(BrowserCredentialRule.Check(Request(SdkKeyKind.Secret, origin)).IsFailure);

    [Fact]
    public void TheRefusal_ShouldSayWhichMistakeWasMade()
    {
        var result = BrowserCredentialRule.Check(Request(SdkKeyKind.Secret, "https://app.example.com"));

        // Deliberately not the uniform "not valid" the other SDK key failures share: the caller
        // already holds this credential, so nothing is disclosed, and somebody who has just wired
        // the wrong key into a web app needs to be told that rather than sent hunting for a typo.
        Assert.Contains("publishable key", result.Error.Message);
        Assert.NotEqual(SdkKeyErrors.TokenMalformed.Message, result.Error.Message);
    }
}
