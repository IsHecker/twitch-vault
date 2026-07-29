namespace TwitchVault.Api.Common.Results;

public enum ErrorType
{
    None,
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