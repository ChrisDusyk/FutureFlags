namespace FutureFlags.Domain.Shared;

public static class ResultExtensions
{
    public static Result<TOut> Bind<TIn, TOut>(this Result<TIn> result, Func<TIn, Result<TOut>> bind) =>
        result.IsSuccess ? bind(result.Value) : Result.Failure<TOut>(result.Error);

    public static Result Bind<TIn>(this Result<TIn> result, Func<TIn, Result> bind) =>
        result.IsSuccess ? bind(result.Value) : Result.Failure(result.Error);

    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> map) =>
        result.IsSuccess ? Result.Success(map(result.Value)) : Result.Failure<TOut>(result.Error);

    public static Result<TIn> Tap<TIn>(this Result<TIn> result, Action<TIn> action)
    {
        if (result.IsSuccess)
            action(result.Value);

        return result;
    }

    public static Result Tap(this Result result, Action action)
    {
        if (result.IsSuccess)
            action();

        return result;
    }

    public static Result<TIn> Ensure<TIn>(this Result<TIn> result, Func<TIn, bool> predicate, Error error) =>
        result.IsSuccess && !predicate(result.Value) ? Result.Failure<TIn>(error) : result;

    public static TOut Match<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        result.IsSuccess ? onSuccess(result.Value) : onFailure(result.Error);

    public static TOut Match<TOut>(this Result result, Func<TOut> onSuccess, Func<Error, TOut> onFailure) =>
        result.IsSuccess ? onSuccess() : onFailure(result.Error);
}
