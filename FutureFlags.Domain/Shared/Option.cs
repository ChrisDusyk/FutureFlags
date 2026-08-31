namespace FutureFlags.Domain.Shared;

public readonly struct Option<TValue> : IEquatable<Option<TValue>>
{
    private readonly TValue? _value;
    public bool IsSome { get; }
    public bool IsNone => !IsSome;

    private Option(TValue value)
    {
        _value = value;
        IsSome = true;
    }

    public static Option<TValue> Some(TValue value) => new(value);
    public static Option<TValue> None => default;

    public static implicit operator Option<TValue>(TValue? value) =>
        value is null ? None : Some(value);

    public TResult Match<TResult>(Func<TValue, TResult> onSome, Func<TResult> onNone) =>
        IsSome ? onSome(_value!) : onNone();

    public Option<TResult> Map<TResult>(Func<TValue, TResult> map) =>
        IsSome ? Option<TResult>.Some(map(_value!)) : Option<TResult>.None;

    public Option<TResult> Bind<TResult>(Func<TValue, Option<TResult>> bind) =>
        IsSome ? bind(_value!) : Option<TResult>.None;

    public TValue Reduce(TValue defaultValue) => IsSome ? _value! : defaultValue;
    public TValue Reduce(Func<TValue> defaultValueFactory) => IsSome ? _value! : defaultValueFactory();

    public bool Equals(Option<TValue> other) =>
        IsSome == other.IsSome && (IsNone || EqualityComparer<TValue>.Default.Equals(_value, other._value));

    public override bool Equals(object? obj) => obj is Option<TValue> other && Equals(other);
    public override int GetHashCode() => IsSome ? _value!.GetHashCode() : 0;

    public static bool operator ==(Option<TValue> left, Option<TValue> right) => left.Equals(right);
    public static bool operator !=(Option<TValue> left, Option<TValue> right) => !left.Equals(right);
}

public static class Option
{
    public static Option<TValue> Some<TValue>(TValue value) => Option<TValue>.Some(value);
    public static Option<TValue> None<TValue>() => Option<TValue>.None;
}
