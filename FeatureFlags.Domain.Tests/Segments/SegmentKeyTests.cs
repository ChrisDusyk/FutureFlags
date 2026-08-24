using FeatureFlags.Domain.Segments;

namespace FeatureFlags.Domain.Tests.Segments;

public class SegmentKeyTests
{
    [Theory]
    [InlineData("staff")]
    [InlineData("beta-testers")]
    [InlineData("beta-testers-v2")]
    [InlineData("v2")]
    [InlineData("123")]
    public void Create_WithValidSlug_ShouldSucceed(string value)
    {
        var result = SegmentKey.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value.Value);
    }

    [Theory]
    [InlineData("Beta-Testers", "beta-testers")]
    [InlineData("  beta-testers  ", "beta-testers")]
    [InlineData("STAFF", "staff")]
    public void Create_ShouldNormalizeCasingAndWhitespace(string input, string expected)
    {
        var result = SegmentKey.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingValue_ShouldFailAsRequired(string? value)
    {
        var result = SegmentKey.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.KeyRequired, result.Error);
    }

    [Theory]
    [InlineData("beta testers")]
    [InlineData("beta_testers")]
    [InlineData("-beta")]
    [InlineData("beta-")]
    [InlineData("beta--testers")]
    [InlineData("beta.testers")]
    public void Create_WithNonSlug_ShouldFailAsInvalidFormat(string value)
    {
        var result = SegmentKey.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.KeyInvalidFormat, result.Error);
    }

    [Fact]
    public void Create_WithAnOverlongKey_ShouldFail()
    {
        var result = SegmentKey.Create(new string('a', SegmentKey.MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal(SegmentErrors.KeyTooLong, result.Error);
    }

    [Fact]
    public void TwoKeysWithTheSameValue_ShouldBeEqual()
    {
        Assert.Equal(SegmentKey.Create("staff").Value, SegmentKey.Create("STAFF").Value);
    }
}
