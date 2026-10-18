namespace VECLauncher.Services;

public readonly struct Result<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => Error is not null;

    private Result(T? value, string? error)
    {
        Value = value;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(string error) => new(default, error);

    public Result<TOut> Map<TOut>(Func<T, TOut> map)
        => IsSuccess ? Result<TOut>.Ok(map(Value!)) : Result<TOut>.Fail(Error!);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
        => IsSuccess ? bind(Value!) : Result<TOut>.Fail(Error!);

    public T Unwrap()
    {
        if (IsFailure) throw new InvalidOperationException($"Result is failure: {Error}");
        return Value!;
    }

    public T UnwrapOrDefault(T defaultValue)
        => IsSuccess ? Value! : defaultValue;

    public static implicit operator Result<T>(T value) => Ok(value);

    public override string ToString()
        => IsSuccess ? $"Ok({Value})" : $"Fail({Error})";
}

public readonly struct Result
{
    public string? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => Error is not null;

    private Result(string? error) => Error = error;

    public static Result Ok() => new(null);
    public static Result Fail(string error) => new(error);

    public void Unwrap()
    {
        if (IsFailure) throw new InvalidOperationException($"Result is failure: {Error}");
    }

    public override string ToString()
        => IsSuccess ? "Ok()" : $"Fail({Error})";
}
