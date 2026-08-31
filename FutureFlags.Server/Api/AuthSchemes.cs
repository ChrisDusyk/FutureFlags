namespace FutureFlags.Server.Api;

/// <summary>
/// The two kinds of credential this API accepts, and the scheme that routes between them.
///
/// <para>
/// Both arrive on the same <c>Authorization: Bearer</c> header, because that is what every HTTP
/// client already knows how to send. They are told apart by shape rather than by trying one and
/// falling back to the other: see <see cref="Any"/>.
/// </para>
/// </summary>
public static class AuthSchemes
{
    /// <summary>
    /// A user's token from the auth service. The console holds one; it lives fifteen minutes.
    /// This is <c>JwtBearerDefaults.AuthenticationScheme</c> under its own name, spelled out here so
    /// the three schemes read together.
    /// </summary>
    public const string Jwt = "Bearer";

    /// <summary>An SDK key. A program holds one; it does not expire and can only read.</summary>
    public const string SdkKey = "SdkKey";

    /// <summary>
    /// The default scheme: it looks at the credential and forwards to one of the other two.
    ///
    /// <para>
    /// Selection is on the <c>ffs_</c> prefix, which is total — a JWT is dot-separated base64url and
    /// cannot begin with it. Deliberately not "try the JWT handler, fall back to the other": that
    /// would double the work on every request, and it would make a rejected credential ambiguous
    /// about which handler rejected it and why.
    /// </para>
    /// </summary>
    public const string Any = "Any";
}
