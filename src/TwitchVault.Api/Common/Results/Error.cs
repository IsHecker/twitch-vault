namespace TwitchVault.Api.Common.Results;

public enum ErrorType
{
    NotFound,
    Validation,
    Unauthorized,
    Conflict,
    Forbidden,
    TooManyRequests,
    Failure,
    Problem,
    Unexpected
}

public record struct Error(string Message, ErrorType Type)
{
    public static readonly Error NoErrors =
        Unexpected("Errors cannot be retrieved from a successful Result.");

    public static readonly Error NullValue =
        new("Null value was provided", ErrorType.Failure);


    public static Error Failure(string message = "A 'failure' error has occurred.") =>
            new(message, ErrorType.Failure);

    public static Error TooManyRequests(string message = "A 'Too many requests' error has occurred.") =>
            new(message, ErrorType.TooManyRequests);

    public static Error Validation(string message = "A 'validation' error has occurred.") =>
            new(message, ErrorType.Validation);

    public static Error Conflict(string message = "A 'conflict' error has occurred.") =>
            new(message, ErrorType.Conflict);

    public static Error NotFound(string message = "A 'Not Found' error has occurred.") =>
            new(message, ErrorType.NotFound);

    public static Error Unauthorized(string message = "An 'Unauthorized' error has occurred.") =>
            new(message, ErrorType.Unauthorized);

    public static Error Forbidden(string message = "A 'Forbidden' error has occurred.") =>
            new(message, ErrorType.Forbidden);

    public static Error Problem(string message = "A problem has occurred.") =>
            new(message, ErrorType.Problem);

    public static Error Unexpected(string message = "An 'unexpected' error has occurred.") =>
            new(message, ErrorType.Unexpected);

    public override readonly string ToString() => $"{Type}: {Message}";
}