using System.Security.Cryptography;
using FutureFlags.Domain.Environments;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.SdkKeys;

/// <summary>
/// A credential a program holds, scoped to one environment.
///
/// <para>
/// This is the counterpart to a user's session: nobody is behind it, it does not expire, and it can
/// only read. What bounds it is the environment it was issued for and the fact that it can be
/// revoked — so it carries a name, because "revoke the one the old CI runner uses" needs the keys
/// to be tellable apart, and a last-used time, because that is what makes revoking an unfamiliar
/// key a decision rather than a gamble.
/// </para>
///
/// Timestamps are supplied by the caller rather than read from a clock, as everywhere else here.
/// </summary>
public sealed class SdkKey
{
    public const int MaxNameLength = 100;

    /// <summary>
    /// How coarsely <see cref="LastUsedAt"/> is kept. Every request that presents a key could write
    /// this, which would turn a read-only endpoint into a write on every call for the sake of a
    /// column nobody reads in real time. An hour's resolution answers the only question it is for —
    /// "is anything still using this?" — at roughly one write per key per hour.
    /// </summary>
    public static readonly TimeSpan LastUsedResolution = TimeSpan.FromHours(1);

    private DateTimeOffset? _lastUsedAt;
    private DateTimeOffset? _revokedAt;

    private SdkKey(
        Guid id,
        string name,
        SdkKeyKind kind,
        EnvironmentKey environment,
        string selector,
        byte[] secretHash,
        Guid createdBy,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Environment = environment;
        Selector = selector;
        SecretHash = secretHash;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    // EF Core materialization only.
    private SdkKey()
    {
        Name = null!;
        Kind = null!;
        Environment = null!;
        Selector = null!;
        SecretHash = null!;
    }

    public Guid Id { get; private set; }

    /// <summary>What a person calls it — "CI", "web app". Not an identifier.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// Whether this key is a secret or something meant to be published. It decides where the key
    /// may be used from, not what it may read — a publishable key sees exactly what a secret one
    /// scoped to the same environment sees.
    /// </summary>
    public SdkKeyKind Kind { get; private set; }

    /// <summary>
    /// The one environment this key can read. It comes from here and nowhere else: a request
    /// carrying an SDK key names no environment, so there is none to get wrong or to escalate.
    /// </summary>
    public EnvironmentKey Environment { get; private set; }

    /// <summary>The public half of the token — see <see cref="SdkKeyToken"/>.</summary>
    public string Selector { get; private set; }

    private byte[] SecretHash { get; set; }

    /// <summary>The user who issued it. Not a foreign key — <c>public.users</c> is a mirror.</summary>
    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When this key was last presented, to <see cref="LastUsedResolution"/>. None means it has
    /// never been used — which is the interesting case when deciding whether anyone would notice.
    /// </summary>
    public Option<DateTimeOffset> LastUsedAt => _lastUsedAt.ToOption();

    /// <summary>None while the key still works.</summary>
    public Option<DateTimeOffset> RevokedAt => _revokedAt.ToOption();

    public bool IsActive => _revokedAt is null;

    /// <summary>
    /// Issues a key, returning the entity to store and the token to hand over — together, because
    /// they are only ever available at the same moment. The token is not recoverable afterwards.
    /// </summary>
    public static Result<IssuedSdkKey> Issue(
        string? name,
        SdkKeyKind kind,
        EnvironmentKey environment,
        Guid createdBy,
        DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<IssuedSdkKey>(SdkKeyErrors.NameRequired);

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
            return Result.Failure<IssuedSdkKey>(SdkKeyErrors.NameTooLong);

        var token = SdkKeyToken.Issue(kind, environment);

        var key = new SdkKey(
            Guid.CreateVersion7(),
            trimmedName,
            kind,
            environment,
            token.Selector,
            token.SecretHash,
            createdBy,
            timestamp);

        return Result.Success(new IssuedSdkKey(key, token.Value));
    }

    /// <summary>
    /// Whether a presented credential is this key's. Compared in fixed time: the selector already
    /// told an attacker which row was hit, and a byte-by-byte comparison that returns early would
    /// let them learn the stored hash a byte at a time from the timing.
    /// </summary>
    public bool Matches(SdkKeyCredential credential) =>
        CryptographicOperations.FixedTimeEquals(SecretHash, credential.SecretHash);

    /// <summary>
    /// Retires the key. Deliberately not a delete: the row is what a revoked key's 401 is explained
    /// by, and deleting it would make a key that stopped working indistinguishable from one that
    /// never existed.
    /// </summary>
    public Result Revoke(DateTimeOffset timestamp)
    {
        if (_revokedAt is not null)
            return Result.Failure(SdkKeyErrors.AlreadyRevoked);

        _revokedAt = timestamp;

        return Result.Success();
    }

    /// <summary>
    /// Records that the key was presented, at most once per <see cref="LastUsedResolution"/>.
    /// Returns whether anything changed, so a caller on the read path only writes when there is
    /// something to write.
    /// </summary>
    public bool MarkUsed(DateTimeOffset timestamp)
    {
        if (_lastUsedAt is { } lastUsed && timestamp - lastUsed < LastUsedResolution)
            return false;

        _lastUsedAt = timestamp;

        return true;
    }
}

/// <summary>
/// A key and its token, which exist together only at the moment of issue.
/// <see cref="Token"/> must reach the response and nothing else — no log, no store, no second read.
/// </summary>
public sealed record IssuedSdkKey(SdkKey Key, string Token);
