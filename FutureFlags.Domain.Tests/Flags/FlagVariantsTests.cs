using FutureFlags.Domain.Flags;
using FutureFlags.Evaluation;

namespace FutureFlags.Domain.Tests.Flags;

public class FlagVariantsTests
{
    [Fact]
    public void Create_ForABooleanFlagWithNothing_ShouldBeTheBooleanPair()
    {
        var result = FlagVariants.Create(FlagValueType.Boolean, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(FlagVariants.BooleanPair, result.Value);
    }

    [Fact]
    public void Create_ForABooleanFlagWithTheBooleanPair_ShouldSucceed()
    {
        var result = FlagVariants.Create(FlagValueType.Boolean,
        [
            new FlagVariant("on", FlagValue.True),
            new FlagVariant("off", FlagValue.False),
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(FlagVariants.BooleanPair, result.Value);
    }

    [Fact]
    public void Create_ForABooleanFlagWithSwappedValues_ShouldBeRefused()
    {
        // The right names and the wrong values is the case a name-by-name check would wave through,
        // and it is the one that would make a flag's answer unpredictable from its name.
        var result = FlagVariants.Create(FlagValueType.Boolean,
        [
            new FlagVariant("on", FlagValue.False),
            new FlagVariant("off", FlagValue.True),
        ]);

        Assert.True(result.IsFailure);
        Assert.Equal("Flag.Variants.BooleanFixed", result.Error.Code);
    }

    [Fact]
    public void Create_ForABooleanFlagWithExtraVariants_ShouldBeRefused()
    {
        var result = FlagVariants.Create(FlagValueType.Boolean,
        [
            new FlagVariant("on", FlagValue.True),
            new FlagVariant("off", FlagValue.False),
            new FlagVariant("maybe", FlagValue.True),
        ]);

        Assert.True(result.IsFailure);
        Assert.Equal("Flag.Variants.BooleanFixed", result.Error.Code);
    }

    [Fact]
    public void Create_WithAnUnnamedVariant_ShouldBeRefused()
    {
        var result = FlagVariants.Create(FlagValueType.Boolean, [new FlagVariant("  ", FlagValue.True)]);

        Assert.True(result.IsFailure);
        Assert.Equal("Flag.Variants.NameRequired", result.Error.Code);
    }

    [Fact]
    public void Create_WithAValueNoRuntimeCanCarry_ShouldBeRefused()
    {
        var result = FlagVariants.Create(FlagValueType.Boolean,
            [new FlagVariant("on", FlagValue.OfNumber(double.NaN))]);

        Assert.True(result.IsFailure);
        Assert.Equal("Flag.Variants.ValueNotRepresentable", result.Error.Code);
    }

    [Fact]
    public void TheNormalForm_ShouldBeDeduplicatedAndOrdinalOrdered()
    {
        var variants = FlagVariants.FromPersisted(
        [
            new FlagVariant("on", FlagValue.True),
            new FlagVariant("off", FlagValue.False),
            new FlagVariant("on", FlagValue.False),
        ]);

        Assert.Equal(["off", "on"], variants.Variants.Select(variant => variant.Name));
    }

    [Fact]
    public void TwoSetsWithTheSameContent_ShouldBeEqual()
    {
        // Every idempotence check on the aggregate compares one of these against another, and a
        // record would compare the list by reference.
        var left = FlagVariants.FromPersisted([new FlagVariant("a", FlagValue.True)]);
        var right = FlagVariants.FromPersisted([new FlagVariant("a", FlagValue.True)]);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void FromPersisted_WithNothing_ShouldBeTheBooleanPair() =>
        Assert.Equal(FlagVariants.BooleanPair, FlagVariants.FromPersisted(null));

    [Fact]
    public void ValueOf_ShouldAnswerNoneForANameNothingCarries()
    {
        Assert.True(FlagVariants.BooleanPair.ValueOf("enabled").IsNone);
        Assert.Equal(FlagValue.True, FlagVariants.BooleanPair.ValueOf("on").Reduce(FlagValue.False));
    }
}
