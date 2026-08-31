using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Tests.Shared;

public class OptionTests
{
    [Fact]
    public void Some_ShouldBeSome()
    {
        var option = Option<int>.Some(42);

        Assert.True(option.IsSome);
        Assert.False(option.IsNone);
    }

    [Fact]
    public void None_ShouldBeNone()
    {
        var option = Option<int>.None;

        Assert.False(option.IsSome);
        Assert.True(option.IsNone);
    }

    [Fact]
    public void ImplicitConversion_FromNull_ShouldProduceNone()
    {
        string? value = null;
        Option<string> option = value;

        Assert.True(option.IsNone);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ShouldProduceSome()
    {
        Option<string> option = "hello";

        Assert.True(option.IsSome);
    }

    [Fact]
    public void Match_ShouldInvokeCorrectBranch()
    {
        var someResult = Option<int>.Some(2).Match(value => $"some:{value}", () => "none");
        var noneResult = Option<int>.None.Match(value => $"some:{value}", () => "none");

        Assert.Equal("some:2", someResult);
        Assert.Equal("none", noneResult);
    }

    [Fact]
    public void Map_OnSome_ShouldTransformValue()
    {
        var option = Option<int>.Some(2).Map(value => value * 2);

        Assert.Equal(4, option.Reduce(0));
    }

    [Fact]
    public void Bind_OnNone_ShouldRemainNone()
    {
        var option = Option<int>.None.Bind(value => Option<int>.Some(value * 2));

        Assert.True(option.IsNone);
    }

    [Fact]
    public void Reduce_OnNone_ShouldReturnDefaultValue()
    {
        var value = Option<int>.None.Reduce(99);

        Assert.Equal(99, value);
    }

    [Fact]
    public void Equality_TwoSomesWithSameValue_ShouldBeEqual()
    {
        Assert.Equal(Option<int>.Some(1), Option<int>.Some(1));
    }

    [Fact]
    public void Equality_NoneAndNone_ShouldBeEqual()
    {
        Assert.Equal(Option<int>.None, Option<int>.None);
    }

    [Fact]
    public void ToResult_OnSome_ShouldReturnSuccess()
    {
        var result = Option<int>.Some(2).ToResult(Error.NotFound("Sample.NotFound", "not found"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
    }

    [Fact]
    public void ToResult_OnNone_ShouldReturnFailure()
    {
        var error = Error.NotFound("Sample.NotFound", "not found");
        var result = Option<int>.None.ToResult(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }
}
