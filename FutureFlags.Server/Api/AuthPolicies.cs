namespace FutureFlags.Server.Api;

/// <summary>
/// The authorization policies every slice draws on.
///
/// <para>
/// The first two are the console's, matching the two roles: <see cref="SignedIn"/> is the ordinary
/// console, <see cref="Admin"/> is managing the organization — its members, and the SDK keys.
/// <see cref="SdkKey"/> is the other kind of caller entirely.
/// </para>
///
/// <para>
/// Each one accepts exactly one authentication scheme, and they do not overlap: a user's token
/// cannot reach the evaluation endpoint and an SDK key cannot reach anything else. That is set up
/// in <see cref="AuthenticationExtensions"/>, where the reasoning is.
/// </para>
/// </summary>
public static class AuthPolicies
{
    public const string SignedIn = "signed-in";
    public const string Admin = "admin";

    /// <summary>A program holding an SDK key. Read-only, and only its own environment.</summary>
    public const string SdkKey = "sdk-key";
}
