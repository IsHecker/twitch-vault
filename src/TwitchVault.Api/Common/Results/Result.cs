namespace TwitchVault.Api.Common.Results;

public readonly struct Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success => new(true, Error.NoErrors);

    public static implicit operator Result(Error error) => new(false, error);

    public static Result Failure(Error error) => new(false, error);
    public static Result<TValue> Failure<TValue>(Error error) => new(error);
}

public readonly struct Result<TValue>
{
    private readonly TValue _value;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public TValue Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("The value of a failure result can't be accessed.");

    public Result(TValue value)
    {
        _value = value;
        IsSuccess = true;
        Error = Error.NoErrors;
    }

    public Result(Error error)
    {
        _value = default!;
        IsSuccess = false;
        Error = error;
    }

    public static implicit operator Result<TValue>(TValue? value) =>
        value is not null ? new(value) : Failure(Error.NullValue);

    public static implicit operator Result<TValue>(Error? error) =>
        error is not null ? new(error.Value) : Failure(Error.NullValue);

    public static implicit operator Result(Result<TValue> result) =>
        result.IsSuccess ? Result.Success : Result.Failure(result.Error);

    public static Result<TValue> Failure(Error error) => new(error);
}

public static class ResultExtensions
{
    public static Result<T> ToResult<T>(this T value)
    {
        return new Result<T>(value);
    }
}