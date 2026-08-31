namespace FutureFlags.Domain.Shared;

public static class OptionExtensions
{
    public static Option<TValue> ToOption<TValue>(this TValue? value) where TValue : class =>
        value is null ? Option<TValue>.None : Option<TValue>.Some(value);

    /// <summary>
    /// The same for a nullable value type. Separate because <c>TValue?</c> means two different
    /// things depending on the constraint — a reference that may be null above, a
    /// <see cref="Nullable{T}"/> here — which is also what keeps these two from colliding.
    /// </summary>
    public static Option<TValue> ToOption<TValue>(this TValue? value) where TValue : struct =>
        value.HasValue ? Option<TValue>.Some(value.Value) : Option<TValue>.None;

    public static Result<TValue> ToResult<TValue>(this Option<TValue> option, Error error) =>
        option.Match(Result.Success, () => Result.Failure<TValue>(error));
}
