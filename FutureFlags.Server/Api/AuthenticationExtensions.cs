using FutureFlags.Domain.SdkKeys;
using FutureFlags.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace FutureFlags.Server.Api;

/// <summary>
/// Wires up the server's half of authentication: it trusts tokens the auth service signed, and
/// nothing else. There is no session store and no per-request call out — a token carries the
/// user's id and role, and the signature is checked against the auth service's public keys.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Must match the values the auth service puts in the tokens it mints (auth/src/config.ts).
    /// Deliberately not derived from a hostname, so neither side needs reconfiguring when a URL
    /// changes; trust comes from the signature, not from these strings.
    /// </summary>
    private const string Issuer = "futureflags-auth";
    private const string Audience = "futureflags-api";

    private const string JwksPath = "/api/auth/jwks";

    public static TBuilder AddConsoleAuthentication<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var authAddress = builder.Configuration.GetAuthServiceAddress();

        builder.Services.AddAuthentication(AuthSchemes.Any)
            // The scheme every endpoint actually runs: it reads the credential and forwards to one
            // of the two below. See AuthSchemes.Any for why this is a shape test and not a retry.
            .AddPolicyScheme(AuthSchemes.Any, displayName: null, options =>
                options.ForwardDefaultSelector = context =>
                    SdkKeyToken.LooksLikeSdkKey(ReadBearerToken(context))
                        ? AuthSchemes.SdkKey
                        : AuthSchemes.Jwt)
            .AddScheme<AuthenticationSchemeOptions, SdkKeyAuthenticationHandler>(
                AuthSchemes.SdkKey, displayName: null, configureOptions: null)
            .AddJwtBearer(AuthSchemes.Jwt, options =>
            {
                // Keep the claim names the token actually uses. Without this ASP.NET rewrites
                // "sub" and "role" into WS-Federation URIs, and AuthClaims stops lining up.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Issuer,
                    ValidAudience = Audience,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = AuthClaims.Email,
                    RoleClaimType = AuthClaims.Role,
                    // Tokens live 15 minutes; there is no reason to honour a stale one for longer.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // The auth service publishes a bare JWKS rather than an OpenID discovery
                // document, so the metadata is assembled from those keys directly. The
                // configuration manager still handles caching and periodic refresh, which is
                // what makes key rotation a non-event here.
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    $"{authAddress.TrimEnd('/')}{JwksPath}",
                    new JwksConfigurationRetriever(),
                    new HttpDocumentRetriever { RequireHttps = false });
            });

        // Every policy names the scheme it accepts, and that is load-bearing rather than tidy.
        // RequireAuthenticatedUser() is satisfied by *any* authenticated principal, so without
        // AddAuthenticationSchemes an SDK key would pass SignedIn and be handed the whole console
        // API. Pinning them here closes that for every existing slice at once, and for every slice
        // written afterwards — a new endpoint gets it by asking for a policy, which it must anyway.
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.SignedIn, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Jwt)
                .RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.Admin, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Jwt)
                .RequireAuthenticatedUser()
                .RequireRole(UserRole.Admin.Value))
            .AddPolicy(AuthPolicies.SdkKey, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.SdkKey)
                .RequireAuthenticatedUser());

        return builder;
    }

    /// <summary>
    /// The bearer token as presented, or null. Only enough parsing to tell the two credential kinds
    /// apart — whichever handler it forwards to does the real work.
    /// </summary>
    private static string? ReadBearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }

    /// <summary>
    /// The auth service's base address, as Aspire's <c>WithReference(auth)</c> injects it, or as
    /// <c>FUTUREFLAGS_AUTH_URL</c> supplies it in a self-hosted deployment. Absent means neither
    /// happened, which is worth failing over immediately rather than at somebody's first sign-in.
    /// </summary>
    public static string GetAuthServiceAddress(this IConfiguration configuration) =>
        configuration["services:auth:http:0"]
        ?? throw new InvalidOperationException(
            "The auth service address is not configured. Run the app through the Aspire AppHost, " +
            "or set FUTUREFLAGS_AUTH_URL to the auth service's base address.");
}

/// <summary>
/// Turns the auth service's JWKS into the metadata the JWT handler expects. Only the signing
/// keys matter — everything else in an OpenID configuration is unused here.
/// </summary>
internal sealed class JwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        var document = await retriever.GetDocumentAsync(address, cancel);

        var configuration = new OpenIdConnectConfiguration
        {
            JwksUri = address,
            JsonWebKeySet = new JsonWebKeySet(document)
        };

        foreach (var key in configuration.JsonWebKeySet.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }
}
