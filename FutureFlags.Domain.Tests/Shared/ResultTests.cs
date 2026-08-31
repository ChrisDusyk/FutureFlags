using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Tests.Shared;

public class ResultTests
{
    private static readonly Error SampleError = Error.Failure("Sample.Error", "Something went wrong.");

    [Fact]
    public void Success_ShouldHaveNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_ShouldExposeError()
    {
        var result = Result.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void GenericSuccess_ShouldExposeValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_AccessingValue_ShouldThrow()
    {
        var result = Result.Failure<int>(SampleError);

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ShouldProduceSuccess()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Bind_OnSuccess_ShouldChainToNextResult()
    {
        var result = Result.Success(2)
            .Bind(value => Result.Success(value * 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value);
    }

    [Fact]
    public void Bind_OnFailure_ShouldShortCircuit()
    {
        var called = false;

        var result = Result.Failure<int>(SampleError)
            .Bind(value =>
            {
                called = true;
                return Result.Success(value * 2);
            });

        Assert.False(called);
        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void Map_OnSuccess_ShouldTransformValue()
    {
        var result = Result.Success(2).Map(value => value.ToString());

        Assert.True(result.IsSuccess);
        Assert.Equal("2", result.Value);
    }

    [Fact]
    public void Ensure_WhenPredicateFails_ShouldReturnFailure()
    {
        var result = Result.Success(2).Ensure(value => value > 10, SampleError);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void Match_ShouldInvokeCorrectBranch()
    {
        var successMessage = Result.Success(2).Match(value => $"ok:{value}", error => $"error:{error.Code}");
        var failureMessage = Result.Failure<int>(SampleError).Match(value => $"ok:{value}", error => $"error:{error.Code}");

        Assert.Equal("ok:2", successMessage);
        Assert.Equal($"error:{SampleError.Code}", failureMessage);
    }
}
