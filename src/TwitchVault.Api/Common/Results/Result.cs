namespace TwitchVault.Api.Common.Results;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; private set; }

    protected Result()
    {
        IsSuccess = true;
        Error = Error.NoErrors;
    }

    protected Result(Error error)
    {
        IsSuccess = false;
        Error = error;
    }


    public static Result Success { get; } = new();


    public static implicit operator Result(Error error) => new(error);
    public static Result Failure(Error error) => new(error);
    public static Result<TValue> Failure<TValue>(Error error) => new(error);

    public TResult Match<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        return IsSuccess ? onSuccess() : onFailure(Error);
    }
}

public sealed class Result<TValue> : Result
{
    private readonly TValue _value;

    public TValue Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("The value of a failure result can't be accessed.");

    public Result(TValue value)
    {
        _value = value;
    }

    public Result(Error error) : base(error)
    {
        _value = default!;
    }


    public static implicit operator Result<TValue>(TValue? value) =>
        value is not null ? new(value) : Failure(Error.NullValue);

    public static implicit operator Result<TValue>(Error? error) =>
        error is not null ? new(error.Value) : Failure(Error.NullValue);

    public static new Result<TValue> Failure(Error error) => new(error);

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(_value) : onFailure(Error);
    }
}

public static class ResultExtensions
{
    public static Result<T> ToResult<T>(this T value)
    {
        return new Result<T>(value);
    }
}