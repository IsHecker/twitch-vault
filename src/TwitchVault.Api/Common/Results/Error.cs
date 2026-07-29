namespace TwitchVault.Api.Common.Results;

public record struct Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error NoErrors =
        Unexpected("General.NoErrors", "Errors cannot be retrieved from a successful Result.");

    public static readonly Error NullValue =
        new("General.Null", "Null value was provided", ErrorType.Failure);


    public static Error Failure(string code = "General.Failure",
        string description = "A 'failure' error has occurred.") =>
            new(code, description, ErrorType.Failure);

    public static Error TooManyRequests(string code = "General.TooManyRequests",
        string description = "Too many requests. Please slow down and try again later.") =>
            new(code, description, ErrorType.TooManyRequests);

    public static Error Validation(string code = "General.Validation",
        string description = "A 'validation' error has occurred.") =>
            new(code, description, ErrorType.Validation);

    public static Error Conflict(string code = "General.Conflict",
        string description = "A 'conflict' error has occurred.") =>
            new(code, description, ErrorType.Conflict);

    public static Error NotFound(string code = "General.NotFound",
        string description = "A 'Not Found' error has occurred.") =>
            new(code, description, ErrorType.NotFound);

    public static Error Unauthorized(string code = "General.Unauthorized",
        string description = "An 'Unauthorized' error has occurred.") =>
            new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code = "General.Forbidden",
        string description = "A 'Forbidden' error has occurred.") =>
            new(code, description, ErrorType.Forbidden);

    public static Error Problem(string code = "General.Problem",
        string description = "A problem has occurred.") =>
            new(code, description, ErrorType.Problem);

    public static Error Unexpected(string code = "General.Unexpected",
        string description = "An 'unexpected' error has occurred.") =>
            new(code, description, ErrorType.Unexpected);

    public override readonly string ToString() => $"{Code}: {Description}";
}