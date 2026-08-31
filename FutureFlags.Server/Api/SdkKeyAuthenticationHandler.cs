using System.Security.Claims;
using System.Text.Encodings.Web;
using FutureFlags.Domain.SdkKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FutureFlags.Server.Api;

/// <summary>
/// Authenticates a program rather than a person.
///
/// <para>
/// Unlike the JWT handler, this one has to touch the database — an SDK key is a stored credential
/// with no signature to check and no expiry to read. That is the cost of a credential that can be
/// revoked, and it is one indexed lookup on a column of eleven characters.
/// </para>
///
/// <para>
/// The principal it produces carries an environment and nothing else: no subject, no role. That is
/// what stops an SDK key from satisfying <see cref="AuthPolicies.SignedIn"/> on its own —
/// though the policies do not rely on it, and pin their schemes as well.
/// </para>
/// </summary>
internal sealed class SdkKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISdkKeyRepository repository,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryGetBearerToken(out var token))
        {
            // No credential at all is not a failed authentication — it is an anonymous request, and
            // saying so is what lets an endpoint that allows anonymous callers answer one.
            return AuthenticateResult.NoResult();
        }

        var credential = SdkKeyToken.Parse(token);
        if (credential.IsFailure)
        {
            return AuthenticateResult.Fail(credential.Error.Message);
        }

        var found = await repository.GetBySelectorAsync(
            credential.Value.Selector,
            Context.RequestAborted);

        return await found.Match(
            key => VerifyAsync(key, credential.Value),
            // An unknown selector and a wrong secret answer identically, and so does a revoked key
            // below: which of the three it was is information about a credential the caller does
            // not hold.
            () => Task.FromResult(AuthenticateResult.Fail(SdkKeyErrors.TokenMalformed.Message)));
    }

    private async Task<AuthenticateResult> VerifyAsync(SdkKey key, SdkKeyCredential credential)
    {
        if (!key.Matches(credential) || !key.IsActive)
        {
            return AuthenticateResult.Fail(SdkKeyErrors.TokenMalformed.Message);
        }

        await RecordUseAsync(key);

        var identity = new ClaimsIdentity(
            [
                new Claim(AuthClaims.SdkKeyId, key.Id.ToString()),
                new Claim(AuthClaims.SdkKeyName, key.Name),
                new Claim(AuthClaims.SdkKeyKind, key.Kind.Value),
                new Claim(AuthClaims.Environment, key.Environment.Value)
            ],
            AuthSchemes.SdkKey,
            AuthClaims.SdkKeyName,
            AuthClaims.Role);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), AuthSchemes.SdkKey));
    }

    /// <summary>
    /// Records the use, at the coarse resolution <see cref="SdkKey.LastUsedResolution"/> sets, and
    /// never at the cost of the request. A key that works but whose last-used time could not be
    /// written is still a key that works — failing the call would turn a bookkeeping problem into
    /// an outage for everyone holding one.
    /// </summary>
    private async Task RecordUseAsync(SdkKey key)
    {
        if (!key.MarkUsed(timeProvider.GetUtcNow()))
        {
            return;
        }

        try
        {
            await repository.SaveChangesAsync(Context.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogWarning(
                exception,
                "Could not record last use of SDK key {SdkKeyId}.",
                key.Id);
        }
    }

    private bool TryGetBearerToken(out string? token)
    {
        token = null;

        var header = Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(header) ||
            !header.StartsWith($"{JwtBearerScheme} ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = header[(JwtBearerScheme.Length + 1)..].Trim();

        return token.Length > 0;
    }

    /// <summary>
    /// The <c>Bearer</c> of the header, not the name of a scheme — an SDK key travels as a bearer
    /// token like any other.
    /// </summary>
    private const string JwtBearerScheme = "Bearer";

    /// <summary>
    /// Answers a rejected credential with the challenge a bearer-token caller expects, rather than
    /// the bare 401 the base handler writes.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.Append(HeaderNames.WWWAuthenticate, JwtBearerScheme);

        return Task.CompletedTask;
    }
}
