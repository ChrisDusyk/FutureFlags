using FeatureFlags.Domain.SdkKeys;
using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Server.Api;

/// <summary>
/// Refuses a publishable key on a route that ships segment definitions.
///
/// <para>
/// The counterpart to <see cref="BrowserCredentialRule"/>, and the same shape for the same reason:
/// it is checked in the endpoint rather than expressed as an authorization policy, because a policy
/// requirement that fails produces a bare 403 with no body. A client reading that reports "the
/// server rejected this SDK key, it may have been revoked" — which sends a developer hunting for a
/// revocation when the real answer is that publishable keys evaluate through a different route.
/// </para>
/// <para>
/// This is about what a key may <em>read</em>, where <see cref="BrowserCredentialRule"/> is about
/// where it may be used <em>from</em>. Both run on the ruleset route and neither substitutes for
/// the other: a secret key presented from a browser is still refused, and a publishable key is
/// refused whether or not a browser sent it.
/// </para>
/// </summary>
public static class SecretCredentialRule
{
    public static Result RequireSecret(HttpContext context) =>
        context.User.HasPublishableSdkKey()
            ? Result.Failure(SdkKeyErrors.PublishableKeyForRuleset)
            : Result.Success();
}
