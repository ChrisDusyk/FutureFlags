using FutureFlags.Domain.Environments;
using FutureFlags.Domain.SdkKeys;

namespace FutureFlags.Domain.Tests.SdkKeys;

public class SdkKeyTokenTests
{
    [Fact]
    public void Issue_ShouldProduceTheDocumentedShape()
    {
        var token = SdkKeyToken.Issue(SdkKeyKind.Secret, EnvironmentKey.Development);

        var segments = token.Value.Split('_');

        Assert.Equal(4, segments.Length);
        Assert.Equal(SdkKeyKind.Secret.TokenPrefix, segments[0]);
        Assert.Equal(EnvironmentKey.Development.Value, segments[1]);
        Assert.Equal(token.Selector, segments[2]);
    }

    /// <summary>
    /// The lengths the regex in <see cref="SdkKeyToken"/> spells out as literals, because an
    /// attribute argument cannot reference a constant expression. This is what holds the two
    /// together: change the byte counts and this fails rather than every token silently failing
    /// to parse.
    /// </summary>
    [Fact]
    public void Issue_ShouldProduceSegmentsOfTheLengthsTheParserExpects()
    {
        var token = SdkKeyToken.Issue(SdkKeyKind.Secret, EnvironmentKey.Production);

        var segments = token.Value.Split('_');

        Assert.Equal(SdkKeyToken.SelectorLength, segments[2].Length);
        Assert.Equal(SdkKeyToken.SecretLength, segments[3].Length);
    }

    [Fact]
    public void Issue_ShouldProduceATokenThatParses()
    {
        var token = SdkKeyToken.Issue(SdkKeyKind.Secret, EnvironmentKey.Staging);

        var parsed = SdkKeyToken.Parse(token.Value);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(token.Selector, parsed.Value.Selector);
        Assert.Equal(token.SecretHash, parsed.Value.SecretHash);
    }

    /// <summary>
    /// The regression that made this format hex. The segments were base64url, whose alphabet
    /// contains the underscore the segments are separated by — so roughly one token in ten split
    /// into the wrong number of pieces and could never be presented successfully. One sample is not
    /// enough to catch that; the point of this test is the count.
    /// </summary>
    [Fact]
    public void Issue_ShouldAlwaysProduceATokenThatParses()
    {
        foreach (var environment in EnvironmentKey.All)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var token = SdkKeyToken.Issue(SdkKeyKind.Secret, environment);

                var parsed = SdkKeyToken.Parse(token.Value);

                Assert.True(parsed.IsSuccess, $"'{token.Value}' did not parse.");
                Assert.Equal(token.Selector, parsed.Value.Selector);
            }
        }
    }

    [Fact]
    public void Issue_ShouldNotRepeatItself()
    {
        var tokens = Enumerable.Range(0, 50)
            .Select(_ => SdkKeyToken.Issue(SdkKeyKind.Secret, EnvironmentKey.Development))
            .ToList();

        Assert.Equal(50, tokens.Select(token => token.Selector).Distinct().Count());
        Assert.Equal(50, tokens.Select(token => token.Value).Distinct().Count());
    }

    [Fact]
    public void Issue_ShouldNotStoreTheSecret()
    {
        var token = SdkKeyToken.Issue(SdkKeyKind.Secret, EnvironmentKey.Development);

        var secret = token.Value.Split('_')[3];

        // The hash is what survives; nothing that can be turned back into the token does.
        Assert.DoesNotContain(secret, Convert.ToBase64String(token.SecretHash));
        Assert.Equal(32, token.SecretHash.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-token")]
    [InlineData("ffs_dev")]
    [InlineData("ffs_dev_tooshort_secret")]
    [InlineData("eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiIxIn0.signature")]
    public void Parse_WithAMalformedToken_ShouldFail(string? value)
    {
        var result = SdkKeyToken.Parse(value);

        Assert.True(result.IsFailure);
        Assert.Equal(SdkKeyErrors.TokenMalformed.Code, result.Error.Code);
    }

    [Fact]
    public void Parse_WithAnExtraSegment_ShouldFail()
    {
        var token = SdkKeyToken.Issue(SdkKeyKind.Secret, EnvironmentKey.Development);

        Assert.True(SdkKeyToken.Parse($"{token.Value}_extra").IsFailure);
    }

    [Fact]
    public void Parse_ShouldHashTheSecretItWasGiven()
    {
        var first = SdkKeyToken.Issue(SdkKeyKind.Secret, EnvironmentKey.Development);
        var second = SdkKeyToken.Issue(SdkKeyKind.Secret, EnvironmentKey.Development);

        // Same shape, different secret: the hashes have to differ, or verification means nothing.
        var swapped = $"{SdkKeyKind.Secret.TokenPrefix}_dev_{first.Selector}_{second.Value.Split('_')[3]}";

        var parsed = SdkKeyToken.Parse(swapped);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(first.Selector, parsed.Value.Selector);
        Assert.NotEqual(first.SecretHash, parsed.Value.SecretHash);
    }

    [Theory]
    [InlineData("ffs_dev_abc", true)]
    [InlineData("ffs_", true)]
    [InlineData("ffx_dev_abc", true)]
    [InlineData("ff_dev_abc", false)]
    [InlineData("ffsx_dev_abc", false)]
    [InlineData("eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiIxIn0.signature", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeSdkKey_ShouldTellTheTwoCredentialKindsApart(string? value, bool expected) =>
        Assert.Equal(expected, SdkKeyToken.LooksLikeSdkKey(value));

    /// <summary>
    /// Routing has to be at least as permissive as parsing, or a token this build cannot name the
    /// kind of would be handed to the JWT handler and rejected without its row ever being read —
    /// which is the one thing the prefix is explicitly not allowed to decide.
    /// </summary>
    [Fact]
    public void LooksLikeSdkKey_ShouldAcceptAnyPrefixThatParses()
    {
        var token = SdkKeyToken.Issue(SdkKeyKind.Secret, EnvironmentKey.Development);

        var unknownKind = string.Concat("ffx", token.Value.AsSpan(3));

        Assert.True(SdkKeyToken.Parse(unknownKind).IsSuccess);
        Assert.True(SdkKeyToken.LooksLikeSdkKey(unknownKind));
    }

    /// <summary>
    /// The environment segment is decoration — the row decides. A token naming an environment this
    /// build has never heard of still parses, so retiring an environment cannot silently
    /// invalidate keys the database still considers fine.
    /// </summary>
    [Fact]
    public void Parse_ShouldNotJudgeTheEnvironmentSegment()
    {
        var token = SdkKeyToken.Issue(SdkKeyKind.Secret, EnvironmentKey.Development);

        var renamed = token.Value.Replace("_dev_", "_retired-environment_");

        Assert.True(SdkKeyToken.Parse(renamed).IsSuccess);
    }
}
