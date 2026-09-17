using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Infrastructure.Middleware;

public static class StreamThumbnailsMiddlewareExtensions
{
    public static IApplicationBuilder UseStreamThumbnails(this WebApplication app)
    {
        var paths = app.Services.GetRequiredService<IOptions<PathsOptions>>().Value;
        Directory.CreateDirectory(paths.Streams);

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings.Clear();
        contentTypeProvider.Mappings[".jpg"] = "image/jpeg";
        contentTypeProvider.Mappings[".jpeg"] = "image/jpeg";
        contentTypeProvider.Mappings[".png"] = "image/png";

        return app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, paths.Streams)),
            RequestPath = $"/{paths.Streams}",
            ContentTypeProvider = contentTypeProvider,
            ServeUnknownFileTypes = false
        });
    }
}