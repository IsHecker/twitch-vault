using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Serilog;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Endpoints;
using TwitchVault.Api.Infrastructure.Middleware;
using TwitchVault.Api.Infrastructure.Swagger;
using TwitchVault.Api.Models;

namespace TwitchVault.Api;

public class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, loggerConfig) =>
            loggerConfig.ReadFrom.Configuration(context.Configuration));

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerDocumentation();
        builder.Services.AddTwitchVaultServices(builder.Configuration);

        builder.Services.ConfigureHttpJsonOptions(opts =>
            opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.AddCors(opts =>
            opts.AddDefaultPolicy(policy =>
                policy.AllowAnyHeader()
                    .AllowAnyOrigin()
                    .AllowAnyMethod()));

        var app = builder.Build();

        app.UseSerilogRequestLogging();
        app.UseCors();

        app.UseMiddleware<GlobalExceptionMiddleware>();

        app.UseSwagger();
        app.UseSwaggerUI();

        var paths = app.Services.GetRequiredService<IOptions<PathsOptions>>().Value;
        Directory.CreateDirectory(paths.Streams);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath, paths.Streams)),
            RequestPath = $"/{paths.Streams}"
        });

        app.UseHttpsRedirection();
        app.MapEndpoints();
        app.Run();
    }
}