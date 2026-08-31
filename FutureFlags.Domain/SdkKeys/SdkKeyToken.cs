using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.SdkKeys;

/// <summary>
/// The credential a program holds, and the only place its format is decided.
///
/// <code>ffs_dev_9f2a71c0d4e83b16_4c1e…64 hex characters…9ab7</code>
///
/// <para>
/// Four segments. The leading one is the <see cref="SdkKeyKind.TokenPrefix"/> — <c>ffs</c> for a
/// secret key, <c>ffp</c> for a publishable one — which does two jobs: it lets the server tell an
/// SDK key from a JWT before doing any work with either, and it tells whoever is reading a
/// configuration file which of the two they are looking at. The environment segment is there for
/// that reader alone. <b>Neither it nor the prefix is authoritative</b>, because both arrive from
/// the caller: what a key is, and where it may read, come from its row. The selector is a public
/// lookup handle. The secret is the credential.
/// </para>
///
/// <para>
/// Splitting the selector from the secret is what lets the secret be hashed at rest and still be
/// found in one indexed query. Looking a key up by a hash of the whole token would work too, but
/// only until the hash needed changing.
/// </para>
/// </summary>
public sealed partial class SdkKeyToken
{
    private const int SelectorBytes = 8;
    private const int SecretBytes = 32;

    /// <summary>Lowercase hex of <see cref="SelectorBytes"/> bytes.</summary>
    public const int SelectorLength = SelectorBytes * 2;

    /// <summary>Lowercase hex of <see cref="SecretBytes"/> bytes.</summary>
    public const int SecretLength = SecretBytes * 2;

    private SdkKeyToken(string value, string selector, byte[] secretHash)
    {
        Value = value;
        Selector = selector;
        SecretHash = secretHash;
    }

    /// <summary>
    /// The whole token, as the holder must present it. This exists exactly once, on the way out of
    /// the endpoint that issued it — nothing stores it.
    /// </summary>
    public string Value { get; }

    /// <summary>The public handle the key is looked up by. Stored in plaintext, indexed, unique.</summary>
    public string Selector { get; }

    /// <summary>What is stored in place of the secret.</summary>
    public byte[] SecretHash { get; }

    /// <summary>
    /// Mints a new token. The secret is 256 bits from <see cref="RandomNumberGenerator"/>, which is
    /// why a plain SHA-256 is the right hash for it: there is no password here to guess, no
    /// dictionary to run, and nothing for a slow KDF to protect against. Reach for Argon2 when a
    /// human chose the secret.
    /// </summary>
    public static SdkKeyToken Issue(SdkKeyKind kind, EnvironmentKey environment)
    {
        // Hex, not base64url, and that is not an aesthetic choice: base64url's alphabet includes
        // the underscore this format separates its segments with, so roughly one token in ten would
        // have split into the wrong number of pieces. Hex has no character in common with the
        // separator, which makes the format unambiguous rather than usually-unambiguous.
        var selector = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SelectorBytes));
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));

        return new SdkKeyToken(
            $"{kind.TokenPrefix}_{environment.Value}_{selector}_{secret}",
            selector,
            HashSecret(secret));
    }

    /// <summary>
    /// Whether a presented credential is shaped like an SDK key at all. Cheap and total: a JWT's
    /// first segment is base64url of <c>{"alg"…</c> and so always begins <c>ey</c>, which no prefix
    /// of this shape can, so the server can route a credential to the right authentication scheme
    /// without parsing it or touching the database.
    ///
    /// <para>
    /// Matched on the same loose prefix shape as <see cref="TokenPattern"/>, and deliberately not on
    /// <see cref="SdkKeyKind.All"/>: a token whose kind this build does not know has to reach the
    /// scheme that can look its row up, not the JWT handler that can only reject it. Routing is the
    /// one decision made before the row is read, so it has to be the loosest of the three.
    /// </para>
    /// </summary>
    public static bool LooksLikeSdkKey(string? value) =>
        value is not null && PrefixPattern().IsMatch(value);

    /// <summary>
    /// Splits a presented token into the parts needed to verify it. Every malformed token fails the
    /// same way on purpose — a caller learning <em>which</em> part it got wrong learns something
    /// about a credential it does not hold.
    /// </summary>
    public static Result<SdkKeyCredential> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !TokenPattern().IsMatch(value))
            return Result.Failure<SdkKeyCredential>(SdkKeyErrors.TokenMalformed);

        var segments = value.Split('_');

        return Result.Success(new SdkKeyCredential(
            segments[2],
            HashSecret(segments[3])));
    }

    /// <summary>
    /// Hashes the secret's own text rather than the bytes behind it. Two different strings can
    /// decode to the same bytes under a lax base64 reader; the text of a token we minted cannot be
    /// ambiguous about itself.
    /// </summary>
    private static byte[] HashSecret(string secret) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>
    /// The environment segment is matched loosely — anything slug-shaped — because it is decoration.
    /// Pinning it to <see cref="EnvironmentKey.All"/> would make retiring an environment silently
    /// invalidate keys that the database still says are fine. The prefix is matched the same way,
    /// for the same reason: a token whose kind this build does not know still parses, and the row
    /// is what decides.
    /// <para>
    /// The two lengths are <see cref="SelectorLength"/> and <see cref="SecretLength"/>, and the
    /// prefixes are <see cref="SdkKeyKind.TokenPrefix"/>, all spelled out because an attribute
    /// argument has to be a literal — <c>SdkKeyTokenTests</c> holds them to each other.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"^ff[a-z]_[a-z0-9-]+_[a-f0-9]{16}_[a-f0-9]{64}$")]
    private static partial Regex TokenPattern();

    /// <summary>
    /// The leading shape of <see cref="TokenPattern"/>, spelled out again because an attribute
    /// argument has to be a literal. <c>SdkKeyTokenTests</c> holds the two to each other: anything
    /// this matches must be able to go on and match the whole pattern.
    /// </summary>
    [GeneratedRegex(@"^ff[a-z]_")]
    private static partial Regex PrefixPattern();
}

/// <summary>
/// A presented token, taken apart. Carries the hash rather than the secret so that nothing past
/// <see cref="SdkKeyToken.Parse"/> holds the credential itself.
/// </summary>
public sealed record SdkKeyCredential(string Selector, byte[] SecretHash);
