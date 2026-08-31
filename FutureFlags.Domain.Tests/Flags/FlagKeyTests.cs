using FutureFlags.Domain.Flags;
using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Tests.Flags;

public class FlagKeyTests
{
    [Theory]
    [InlineData("checkout")]
    [InlineData("new-checkout")]
    [InlineData("new-checkout-v2")]
    [InlineData("v2")]
    [InlineData("123")]
    public void Create_WithValidSlug_ShouldSucceed(string value)
    {
        var result = FlagKey.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value.Value);
    }

    [Theory]
    [InlineData("New-Checkout", "new-checkout")]
    [InlineData("  new-checkout  ", "new-checkout")]
    [InlineData("CHECKOUT", "checkout")]
    public void Create_ShouldNormalizeCasingAndWhitespace(string input, string expected)
    {
        var result = FlagKey.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingValue_ShouldFailAsRequired(string? value)
    {
        var result = FlagKey.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.KeyRequired, result.Error);
    }

    [Theory]
    [InlineData("new_checkout")]
    [InlineData("new checkout")]
    [InlineData("-checkout")]
    [InlineData("checkout-")]
    [InlineData("new--checkout")]
    [InlineData("checkout!")]
    [InlineData("chèckout")]
    public void Create_WithNonSlugValue_ShouldFailAsInvalidFormat(string value)
    {
        var result = FlagKey.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.KeyInvalidFormat, result.Error);
    }

    [Fact]
    public void Create_WhenLongerThanMaxLength_ShouldFailAsTooLong()
    {
        var result = FlagKey.Create(new string('a', FlagKey.MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.KeyTooLong, result.Error);
    }

    [Fact]
    public void Create_AtMaxLength_ShouldSucceed()
    {
        var result = FlagKey.Create(new string('a', FlagKey.MaxLength));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_ShouldFailOnLengthBeforeFormat()
    {
        // An over-long value that is also not a slug reports the length problem, since
        // Create checks length first — pinning the order keeps error messages predictable.
        var result = FlagKey.Create(new string('A', FlagKey.MaxLength + 1) + "!");

        Assert.True(result.IsFailure);
        Assert.Equal(FlagErrors.KeyTooLong, result.Error);
    }

    [Fact]
    public void Equality_ShouldBeStructural()
    {
        var first = FlagKey.Create("new-checkout").Value;
        var second = FlagKey.Create("NEW-CHECKOUT").Value;

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void FromPersisted_ShouldBypassValidation()
    {
        var key = FlagKey.FromPersisted("legacy_key");

        Assert.Equal("legacy_key", key.Value);
    }

    [Fact]
    public void ToString_ShouldReturnUnderlyingValue()
    {
        var key = FlagKey.Create("new-checkout").Value;

        Assert.Equal("new-checkout", key.ToString());
    }
}
