using System.Net;
using System.Text.Json;

namespace TwitchVault.Api.Infrastructure.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteProblemResponseAsync(context, ex);
        }
    }

    private static async Task WriteProblemResponseAsync(HttpContext context, Exception ex)
    {
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            title = "An unexpected error occurred.",
            status = 500,
            detail = ex.Message,
            traceId = context.TraceIdentifier
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}