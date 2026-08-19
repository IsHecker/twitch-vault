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

public record struct Error(string Message, ErrorType Type, string? Code = null)
{
    public static readonly Error NoErrors =
        Unexpected("Errors cannot be retrieved from a successful Result.");

    public static readonly Error NullValue =
        new("Null value was provided", ErrorType.Failure);

    public static Error Failure(string message = "A 'failure' error has occurred.") =>
        new(message, ErrorType.Failure);

    public static Error Failure(string code, string message) =>
        new(message, ErrorType.Failure, Code: code);

    public static Error TooManyRequests(string message = "A 'Too many requests' error has occurred.") =>
        new(message, ErrorType.TooManyRequests);

    public static Error TooManyRequests(string code, string message) =>
        new(message, ErrorType.TooManyRequests, Code: code);

    public static Error Validation(string message = "A 'validation' error has occurred.") =>
        new(message, ErrorType.Validation);

    public static Error Validation(string code, string message) =>
        new(message, ErrorType.Validation, Code: code);

    public static Error Conflict(string message = "A 'conflict' error has occurred.") =>
        new(message, ErrorType.Conflict);

    public static Error Conflict(string code, string message) =>
        new(message, ErrorType.Conflict, Code: code);

    public static Error NotFound(string message = "A 'Not Found' error has occurred.") =>
        new(message, ErrorType.NotFound);

    public static Error NotFound(string code, string message) =>
        new(message, ErrorType.NotFound, Code: code);

    public static Error Unauthorized(string message = "An 'Unauthorized' error has occurred.") =>
        new(message, ErrorType.Unauthorized);

    public static Error Unauthorized(string code, string message) =>
        new(message, ErrorType.Unauthorized, Code: code);

    public static Error Forbidden(string message = "A 'Forbidden' error has occurred.") =>
        new(message, ErrorType.Forbidden);

    public static Error Forbidden(string code, string message) =>
        new(message, ErrorType.Forbidden, Code: code);

    public static Error Problem(string message = "A problem has occurred.") =>
        new(message, ErrorType.Problem);

    public static Error Problem(string code, string message) =>
        new(message, ErrorType.Problem, Code: code);

    public static Error Unexpected(string message = "An 'unexpected' error has occurred.") =>
        new(message, ErrorType.Unexpected);

    public static Error Unexpected(string code, string message) =>
        new(message, ErrorType.Unexpected, Code: code);

    public override readonly string ToString() =>
        string.IsNullOrWhiteSpace(Code) ? $"{Type}: {Message}" : $"{Type} ({Code}): {Message}";
}