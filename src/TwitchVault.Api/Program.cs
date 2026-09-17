using System.Text.Json.Serialization;
using Serilog;
using Serilog.Ui.Core.Extensions;
using Serilog.Ui.SqliteDataProvider.Extensions;
using Serilog.Ui.Web.Extensions;
using TwitchLib.EventSub.Webhooks.Extensions;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Endpoints;
using TwitchVault.Api.Infrastructure.Middleware;
using TwitchVault.Api.Infrastructure.Swagger;

namespace TwitchVault.Api;

public class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        SQLitePCL.Batteries.Init();
        builder.Host.UseSerilog((context, services, loggerConfig) =>
            loggerConfig
                .ReadFrom.Services(services)
                .ReadFrom.Configuration(context.Configuration));

        var dbPath = Path.Combine(AppContext.BaseDirectory, "logs.db");
        builder.Services.AddSerilogUi(options =>
            options.UseSqliteServer(config =>
            {
                config.WithConnectionString($"Data Source={dbPath}")
                .WithTable("Logs");
            }));

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerDocumentation();

        builder.Configuration.AddJsonFile($"secrets.json", optional: false, reloadOnChange: true);

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
        app.UseHttpsRedirection();
        app.UseMiddleware<GlobalExceptionMiddleware>();

        app.UseSerilogUi(options =>
        {
            options.WithRoutePrefix("logs");
        });

        app.UseTwitchLibEventSubWebhooks();

        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseRateLimiter();
        app.UseStreamThumbnails();
        app.MapEndpoints(isDevelopment: app.Environment.IsDevelopment());
        app.Run();
    }
}