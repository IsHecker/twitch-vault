using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace TwitchVault.Api.Common.Results;

public static class ResultHttpExtensions
{
    public static IResult ToHttpResult(this Result result)
    {
        if (result.IsSuccess)
            return HttpResults.NoContent();

        return MapErrorToHttpResult(result.Error);
    }

    public static IResult ToHttpResult<TValue>(this Result<TValue> result)
    {
        if (result.IsSuccess)
            return HttpResults.Ok(result.Value);

        return MapErrorToHttpResult(result.Error);
    }

    public static IResult ToHttpResult<TValue>(this Result<TValue> result, Func<TValue, IResult> onSuccess)
    {
        if (result.IsSuccess)
            return onSuccess(result.Value);

        return MapErrorToHttpResult(result.Error);
    }

    public static IResult ToCreatedHttpResult<TValue>(this Result<TValue> result, Func<TValue, string> uriFactory)
    {
        if (result.IsSuccess)
            return HttpResults.Created(uriFactory(result.Value), result.Value);

        return MapErrorToHttpResult(result.Error);
    }

    private static IResult MapErrorToHttpResult(Error error) => error.Type switch
    {
        ErrorType.NotFound => HttpResults.NotFound(error.Message),
        ErrorType.Conflict => HttpResults.Conflict(error.Message),
        ErrorType.Validation => HttpResults.BadRequest(error.Message),
        ErrorType.Failure => HttpResults.BadRequest(error.Message),
        ErrorType.Unauthorized => HttpResults.Unauthorized(),
        ErrorType.Forbidden => HttpResults.Forbid(),
        ErrorType.TooManyRequests => HttpResults.StatusCode(StatusCodes.Status429TooManyRequests),
        ErrorType.Problem or ErrorType.Unexpected => HttpResults.Problem(detail: error.Message),
        _ => HttpResults.BadRequest(error.Message)
    };
}
