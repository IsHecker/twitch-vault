namespace TwitchVault.Api.Endpoints;

internal sealed class Ping : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/ping", () => Results.Ok())
            .AllowAnonymous()
            .WithTags("Health").WithName(nameof(Ping))
            .WithSummary("Returns 200 OK to confirm the API is running.")
            .Produces(StatusCodes.Status200OK);
    }
}