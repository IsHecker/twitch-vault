using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Recording.HLS;

using TwitchVault.Api.Events;
namespace TwitchVault.Api.Endpoints.Logs;


internal sealed class GetLogs : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var logsGroup = app.MapGroup("/api/logs").WithTags("Logs");
        logsGroup.MapGet("/files", (IOptions<PathsOptions> paths, IWebHostEnvironment env) =>
        {
            var logsPath = Path.Combine(env.ContentRootPath, paths.Value.Logs);
            if (!Directory.Exists(logsPath))
            {
                return Results.NotFound("Logs directory not found.");
            }

            var files = Directory.GetFiles(logsPath, "*.log")
                .Select(Path.GetFileName)
                .OrderByDescending(f => f)
                .ToList();
            return Results.Ok(files);
        })
        .WithName("ListLogFiles")
        .WithSummary("Lists available Serilog log files.");
        logsGroup.MapGet("/files/{fileName}", (string fileName, IOptions<PathsOptions> paths, IWebHostEnvironment env) =>
        {
            var logsPath = Path.Combine(env.ContentRootPath, paths.Value.Logs);
            var filePath = Path.Combine(logsPath, fileName);
            if (!File.Exists(filePath))
            {
                return Results.NotFound("Log file not found.");
            }

            return Results.File(filePath, "text/plain");
        })
        .WithName("GetLogFileContent")
        .WithSummary("Returns the content of a specific log file.");
    }
}
