using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.SdkKeys;

/// <summary>
/// Whether a key is a secret or something meant to be published.
///
/// <para>
/// The distinction exists because a browser cannot keep a secret. Code that runs on a server can
/// hold a credential nobody else sees; code shipped to a browser is readable by everyone who
/// receives it, so a key that travels there is public whether or not anyone intended it to be.
/// Pretending otherwise is how a credential ends up in a bundle with only a paragraph of
/// documentation standing between it and a stranger.
/// </para>
///
/// <para>
/// So the two are different things with different prefixes, visible at a glance in a configuration
/// file, and the server enforces the difference rather than describing it: a request that arrives
/// with an <c>Origin</c> header came from a browser, and only a
/// <see cref="Publishable"/> key is accepted from one.
/// </para>
/// </summary>
public sealed record SdkKeyKind
{
    public const int MaxLength = 20;

    /// <summary>
    /// Server-side only. Reads every flag in its environment and is never sent to a browser.
    /// </summary>
    public static readonly SdkKeyKind Secret = new("secret", "ffs");

    /// <summary>
    /// Safe to ship in a browser bundle, in the sense that publishing it is expected rather than a
    /// mistake. It is still an environment-wide read: whoever holds it can see every flag key in
    /// that environment and whether each is on. That is the trade a publishable key makes, and the
    /// console says so where one is issued.
    /// </summary>
    public static readonly SdkKeyKind Publishable = new("publishable", "ffp");

    private SdkKeyKind(string value, string tokenPrefix)
    {
        Value = value;
        TokenPrefix = tokenPrefix;
    }

    public string Value { get; }

    /// <summary>The leading segment of a token of this kind — <c>ffs</c> or <c>ffp</c>.</summary>
    public string TokenPrefix { get; }

    public bool IsPublishable => this == Publishable;

    public static IReadOnlyList<SdkKeyKind> All { get; } = [Secret, Publishable];

    public static Result<SdkKeyKind> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<SdkKeyKind>(SdkKeyErrors.KindRequired);

        var normalized = value.Trim().ToLowerInvariant();
        var kind = All.FirstOrDefault(candidate => candidate.Value == normalized);

        return kind is null
            ? Result.Failure<SdkKeyKind>(SdkKeyErrors.KindUnrecognized(value))
            : Result.Success(kind);
    }

    /// <summary>
    /// The kind a token's leading segment claims. Used only to tell a presented credential apart
    /// from a JWT before anything is looked up — what a key actually <em>is</em> comes from its row.
    /// </summary>
    public static Option<SdkKeyKind> FromTokenPrefix(string prefix) =>
        All.FirstOrDefault(candidate => candidate.TokenPrefix == prefix).ToOption();

    /// <summary>
    /// Rehydrates a kind that has already been validated on its way into storage.
    /// For persistence use only — this deliberately bypasses <see cref="Create"/>.
    /// </summary>
    public static SdkKeyKind FromPersisted(string value) =>
        All.FirstOrDefault(candidate => candidate.Value == value)
        ?? throw new InvalidOperationException($"'{value}' is not an SDK key kind this application recognizes.");

    public override string ToString() => Value;
}
