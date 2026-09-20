using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Features.Logs.Endpoints;

internal sealed class GetLogs : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // All log endpoints require Admin access to prevent information disclosure.
        var logsGroup = app.MapGroup("/api/logs")
            .WithTags("Logs");

        logsGroup.MapGet("/files", (IOptions<PathsOptions> paths, IWebHostEnvironment env) =>
        {
            var logsPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, paths.Value.Logs));
            if (!Directory.Exists(logsPath))
                return Results.NotFound("Logs directory not found.");

            var files = Directory.GetFiles(logsPath, "*.log")
                .Select(Path.GetFileName)
                .OrderByDescending(f => f)
                .ToList();
            return Results.Ok(files);
        })
        .WithName("ListLogFiles")
        .WithSummary("[Admin] Lists available Serilog log files.");

        logsGroup.MapGet("/files/{fileName}", (string fileName, IOptions<PathsOptions> paths, IWebHostEnvironment env) =>
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Invalid log file name.");
            }

            var logsRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, paths.Value.Logs));
            var filePath = Path.GetFullPath(Path.Combine(logsRoot, fileName));

            if (!filePath.StartsWith(logsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Invalid log file name.");

            if (!File.Exists(filePath))
                return Results.NotFound("Log file not found.");

            return Results.File(filePath, "text/plain");
        })
        .WithName("GetLogFileContent")
        .WithSummary("[Admin] Returns the content of a specific log file.");
    }
}